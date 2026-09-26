using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using REFrameworkNET;
using SF6Access.Hooks.WorldTour;
using static SF6Access.Services.WorldTour.LevelRoutePlanner;

namespace SF6Access.Services.WorldTour;

/// <summary>Routes through ladders when the destination's navmesh region is not connected to the player's.
/// Design and runtime evidence: docs/sf6-screens.md, "World Tour — multi-level routes".</summary>
internal static class LevelRoute
{
    internal enum Phase { Idle, Searching, Following, Failed }

    /// <summary>Where to walk now; <see cref="To"/> is a ladder end unless <see cref="Final"/>.
    /// <see cref="Partial"/>: the plan stops at the spot nearest the target's height. <see cref="Link"/>: the ladder, or -1.
    /// <see cref="Blocked"/>: no walk reaches this ladder's foot; the beacon points straight at it.</summary>
    internal readonly record struct Guidance(Phase Phase, Spot To, bool Final, bool Up, int Leg, int LaddersLeft, bool Partial, int Link,
                                             bool Blocked = false);

    internal const float LevelM = LevelRoutePlanner.LevelM;
    private const int QuestionBudget = 48;    // About twelve seconds at FieldNavPath's query rate.
    private const int QueryBudget = 2 * QuestionBudget; // Navmesh queries, including endpoint nudges.
    private const long RetryFailedMs = 30000; // Section streaming can bring other ladders into the scene.
    private const long BrokenLegMs = 3000;    // A leg must stay unreachable this long before replanning.
    private const int MaxReplans = 2;         // A plan that keeps breaking is not a plan.
    private const float NudgeM = 1f;          // About one stride: the floor a ladder serves starts within a step of its end.
    private const float LadderEndMarginM = 1f; // Standing at a ladder end, rail tops included, is not climbing.
    private static float ArriveM => MissionBeaconHooks.ARRIVED_M;
    private static float ReachM => 2f * MissionBeaconHooks.ARRIVED_M;
    private static readonly (float dx, float dz)[] Nudges = { (NudgeM, 0f), (-NudgeM, 0f), (0f, NudgeM), (0f, -NudgeM) };

    private static Phase _phase;
    private static string _key;
    private static Spot _target, _planTarget, _start, _failedFrom;
    private static LevelRoutePlanner _planner;
    private static IReadOnlyList<Leg> _plan;
    private static int _leg, _nudge, _queries, _replans, _budget, _probedLeg = -1;
    private static bool _offFrom, _offTo, _askDirect, _abort, _silent, _retrySweep, _partial, _closest;
    private static long _failedAt, _brokenSince;
    private static ulong _failedLadders;

    /// <summary>Starts a spoken ladder search; the direct walk from the player to the target has just failed.</summary>
    internal static void Begin(string key, Spot target, Spot player, bool askDirect)
    {
        int replans = _phase == Phase.Following && key == _key ? _replans + 1 : 0;
        Start(key, target, player, askDirect, QuestionBudget, silent: false);
        _replans = replans;
        LadderCatalog.BeginSweep();
        API.LogInfo(FormattableString.Invariant(
            $"[SF6Access] Level route: no walk from {Show(player)} to {Show(target)} ({target.Y - player.Y:+0.0;-0.0} m vertical); searching ladders"));
    }

    /// <summary>A partial plan has been climbed: the ladders go no nearer, the rest is up to the player.</summary>
    internal static bool AtClosest => _closest;

    /// <summary>The current phase and, while following a plan, the leg the player is on.</summary>
    internal static Guidance Guide(string key, Spot target, Spot player)
    {
        if (_phase != Phase.Idle && key != _key) Reset();
        _target = target;
        if (_phase != Phase.Following)
            return new Guidance(_phase == Phase.Searching && _silent ? Phase.Failed : _phase, target, true, false, 0, 0, false, -1);

        int leg = Progress(_plan, _leg, player, ArriveM, LevelM);
        if (leg != _leg)
        {
            API.LogInfo($"[SF6Access] Level route: leg {_leg} -> {leg} at {Show(player)}");
            _leg = leg;
            _brokenSince = 0;
        }
        var current = _plan[_leg];
        bool final = current.Link < 0;
        if (final && _partial)
        {
            // The end of a partial plan is the last ladder's top. It still belongs to that ladder until LegResult
            // confirms the player stands on the region it leads to: near the top the avatar can still snap to the
            // floor below, and an approach searched from there led back down a 131 m street route (2026-09-26).
            var ladder = _plan[_leg - 1];
            return new Guidance(Phase.Following, current.To, false, ladder.Up, _leg - 1, 1, true, ladder.Link);
        }
        // A drifting target (a walking NPC, a contact point that follows the player) moves the last leg with it.
        var to = final ? new Spot(current.To.X + target.X - _planTarget.X, current.To.Y + target.Y - _planTarget.Y,
                                  current.To.Z + target.Z - _planTarget.Z) : current.To;
        if (current.Blocked && _probedLeg != _leg)
        {
            _probedLeg = _leg;
            ObstacleProbe.Log(_leg > 0 ? _plan[_leg - 1].Exit : player, current.To);
        }
        return new Guidance(Phase.Following, to, final, current.Up, _leg, _plan.Count - 1 - _leg, _partial, current.Link, current.Blocked);
    }

    /// <summary>Feeds back the current leg's route answer; true means hold, the answer says nothing new.</summary>
    internal static bool LegResult(in FieldNavPath.PathAnswer answer, Spot player)
    {
        if (_phase != Phase.Following || answer.Throttled || !answer.Ok) return false;
        // No walk to a blocked ladder is the expected answer, not a broken plan.
        if (_plan[_leg].Blocked) { _brokenSince = 0; return false; }
        bool end = _partial && _plan[_leg].Link < 0;
        if (answer.Exists)
        {
            _brokenSince = 0;
            if (!end) return false;
            // A walk to the last ladder's top exists: the player stands on the region it leads to.
            API.LogInfo($"[SF6Access] Level route: partial plan climbed at {Show(player)}; {_target.Y - player.Y:+0.0;-0.0} m to the objective's height");
            Fail(player);
            _closest = true;
            return true;
        }
        if (answer.StartOffMesh || OnPlannedLadder(player)) return true;
        // Beside the last ladder's top: still stepping off it.
        if (end && Flat(player, _plan[_leg].To) <= ArriveM / 2) return true;
        long now = Environment.TickCount64;
        if (_brokenSince == 0) _brokenSince = now;
        else if (now - _brokenSince > BrokenLegMs)
        {
            API.LogInfo($"[SF6Access] Level route: leg {_leg} unreachable from {Show(player)}; replan {_replans + 1} of {MaxReplans}");
            // The player may have reached the target's region another way, so the direct walk is asked again.
            if (_replans < MaxReplans) Begin(_key, _target, player, askDirect: true);
            else Fail(player);
        }
        return true;
    }

    /// <summary>Advances a running search by at most one navmesh query; call every frame.</summary>
    internal static void Pump()
    {
        if (!FieldNavPath.Enabled) return;
        if (_phase == Phase.Failed) { Retry(); return; }
        if (_phase != Phase.Searching) return;
        if (_planner == null)
        {
            if (!LadderCatalog.Sweep()) return;
            _planner = new LevelRoutePlanner(_start, _target, LadderCatalog.Links, _budget, _askDirect);
        }
        // Resume the search first so the answer to the last question inside the budget still counts.
        var next = _planner.Next();
        if (next is { } q && !_abort && _queries < QueryBudget) Ask(q);
        else Finish();
    }

    internal static void Reset()
    {
        _phase = Phase.Idle;
        _key = null;
        _planner = null;
        _plan = null;
        _leg = _queries = _replans = 0;
        _probedLeg = -1;
        _brokenSince = 0;
        _abort = _silent = _retrySweep = _partial = _closest = false;
        ResetNudge();
    }

    private static void Start(string key, Spot target, Spot player, bool askDirect, int budget, bool silent)
    {
        Reset();
        _phase = Phase.Searching;
        _key = key;
        _target = _planTarget = target;
        _start = player;
        _askDirect = askDirect;
        _budget = budget;
        _silent = silent;
    }

    /// <summary>After a failure: quietly re-check the direct walk, and the ladders too if they or the player changed.</summary>
    private static void Retry()
    {
        if (Environment.TickCount64 - _failedAt < RetryFailedMs) return;
        if (!_retrySweep) { LadderCatalog.BeginSweep(); _retrySweep = true; }
        if (!LadderCatalog.Sweep()) return;
        var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!me.ok) return;
        var player = new Spot(me.x, me.y, me.z);
        bool changed = LadderCatalog.Signature != _failedLadders || Flat(player, _failedFrom) > ReachM;
        bool closest = _closest;
        Start(_key, _target, player, askDirect: true, changed ? QuestionBudget : 1, silent: true);
        _closest = closest;
    }

    private static void Ask(Question q)
    {
        Spot from = Moved(q.From, _offFrom), to = Moved(q.To, _offTo);
        var a = FieldNavPath.TryConnect(from.X, from.Y, from.Z, to.X, to.Y, to.Z);
        if (a.Throttled) return;
        _queries++;
        // Could not answer (no navmesh agent yet, streaming): ask again; the query budget bounds it.
        if (!a.Ok) return;
        if (!a.Exists && (a.StartOffMesh || a.EndOffMesh))
        {
            if (_nudge == 0) { _offFrom = a.StartOffMesh; _offTo = a.EndOffMesh; }
            if (_nudge < Nudges.Length) { _nudge++; return; }
            // No question can reach a target that stays off the navmesh.
            _abort = q.To == Target && _offTo;
            _planner.Answer(false);
            ResetNudge();
            return;
        }
        bool moved = _nudge > 0;
        _planner.Answer(a.Exists, moved && _offFrom ? from : null, moved && _offTo ? to : null);
        ResetNudge();
    }

    private static Spot Moved(int id, bool off)
    {
        var s = _planner.SpotOf(id);
        if (!off || _nudge == 0) return s;
        var (dx, dz) = Nudges[_nudge - 1];
        return new Spot(s.X + dx, s.Y, s.Z + dz);
    }

    private static void ResetNudge()
    {
        _nudge = 0;
        _offFrom = _offTo = false;
    }

    private static void Finish()
    {
        _plan = _planner.Plan;
        if (_plan != null)
        {
            bool silent = _silent;
            _phase = Phase.Following;
            _leg = 0;
            _silent = _closest = false;
            _partial = _planner.Partial;
            string end = _partial ? "nearest reachable spot" : "objective";
            API.LogInfo($"[SF6Access] Level route: {(_partial ? "partial " : "")}plan after {_planner.Asked} questions: " +
                        string.Join(" -> ", _plan.Select(l => l.Link < 0 ? $"{end} {Show(l.To)}"
                            : $"{(l.Blocked ? "blocked " : "")}ladder {(l.Up ? "up" : "down")} {Show(l.To)} to {Show(l.Exit)}")));
            if (_partial && !silent) LogFailure();
            return;
        }
        var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!_silent) LogFailure();
        Fail(me.ok ? new Spot(me.x, me.y, me.z) : _start);
    }

    private static void Fail(Spot player)
    {
        _phase = Phase.Failed;
        _silent = _retrySweep = false;
        _failedAt = Environment.TickCount64;
        _failedFrom = player;
        _failedLadders = LadderCatalog.Signature;
    }

    /// <summary>The ladders nearest the target and what was learned about each end, for the next investigation.</summary>
    private static void LogFailure()
    {
        const int Shown = 8;
        string why = _abort ? ", target stays off the navmesh" : "";
        var line = new StringBuilder(
            $"[SF6Access] Level route: no {(_planner.Partial ? "full route" : "plan")} after {_planner.Asked} questions ({_queries} queries{why}) over {_planner.LinkCount} ladders");
        var nearest = Enumerable.Range(0, _planner.LinkCount)
            .OrderBy(i => Math.Min(Distance(_planner.SpotOf(LowId(i)), _target), Distance(_planner.SpotOf(HighId(i)), _target)))
            .Take(Shown);
        foreach (int i in nearest)
            line.Append($"; #{i} low {Show(_planner.SpotOf(LowId(i)))} {Relations(LowId(i))}, " +
                        $"high {Show(_planner.SpotOf(HighId(i)))} {Relations(HighId(i))}");
        API.LogInfo(line.ToString());
    }

    private static string Relations(int id) => $"[target {Word(_planner.Known(id, Target))}, player {Word(_planner.Known(Player, id))}]";
    private static string Word(int known) => known switch { 1 => "yes", 0 => "no", _ => "?" };

    /// <summary>Beside a planned ladder and well between its two ends: climbing, so no route answer means anything.</summary>
    private static bool OnPlannedLadder(Spot p)
        => _plan.Any(l => l.Link >= 0 && Flat(p, l.To) <= ArriveM / 2
                          && p.Y > MathF.Min(l.To.Y, l.Exit.Y) + LadderEndMarginM
                          && p.Y < MathF.Max(l.To.Y, l.Exit.Y) - LadderEndMarginM);

    private static string Show(Spot s) => FormattableString.Invariant($"({s.X:F1}, {s.Y:F1}, {s.Z:F1})");
}
