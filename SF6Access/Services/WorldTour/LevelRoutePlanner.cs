using System;
using System.Collections.Generic;

#nullable enable
namespace SF6Access.Services.WorldTour;

/// <summary>Finds walk-and-ladder routes between disconnected navmesh regions, one connectivity question at a time.
/// When no route reaches the target, the plan climbs as near to the target's height as the ladders allow.</summary>
internal sealed class LevelRoutePlanner
{
    /// <summary>One avatar height: the borrowed navmesh agent's AgentHeight measured 2.0 m (2026-09-09).</summary>
    internal const float LevelM = 2f;
    /// <summary>How far from the target, on the flat, a partial plan may end: about one Metro City block. Chapter 2-2's
    /// right ladders stood 17 and 23 m from the boss; the wrong picks were other buildings 85 and 133 m away (2026-09-26).</summary>
    internal const float NeighbourhoodM = 32f;
    /// <summary>A ladder foot this close and level with a reached spot, with no walk to it, is taken as behind an
    /// obstacle the navmesh does not model. Chapter 2-2's sign-blocked walk between its two ladders is 10.5 m.</summary>
    internal const float BlockedHopM = 16f;
    private static readonly bool[] Directions = { true, false };

    internal readonly record struct Spot(float X, float Y, float Z);

    /// <summary>A climbable connection between the standing points at its two ends.</summary>
    internal readonly record struct Link(Spot Low, Spot High);

    /// <summary>Walk to <see cref="To"/>, then take ladder <see cref="Link"/> to <see cref="Exit"/>; the last leg has Link -1.
    /// <see cref="Blocked"/>: the navmesh has no walk to this ladder's foot, though it stands close and level.</summary>
    internal readonly record struct Leg(Spot To, Spot Exit, int Link, bool Up, bool Blocked = false);

    /// <summary>"Can the player walk between these two spots?" by spot id.</summary>
    internal readonly record struct Question(int From, int To);

    internal const int Player = 0, Target = 1;
    private readonly IReadOnlyList<Link> _links;
    private readonly Spot[] _spots;
    private readonly int[] _parent;
    private readonly List<(int a, int b)> _apart = new();
    private readonly IEnumerator<Question> _search;
    private readonly int _budget;
    private bool _answer;

    /// <summary>Starts a search. Without <paramref name="askDirect"/> the caller has already found no direct walk;
    /// with it, the target first needs moving onto the navmesh and the direct walk is asked again.</summary>
    internal LevelRoutePlanner(Spot player, Spot target, IReadOnlyList<Link> links, int budget, bool askDirect = false)
    {
        _links = links;
        _budget = budget;
        _spots = new Spot[2 + 2 * links.Count];
        _spots[Player] = player;
        _spots[Target] = target;
        for (int i = 0; i < links.Count; i++)
        {
            _spots[LowId(i)] = links[i].Low;
            _spots[HighId(i)] = links[i].High;
        }
        _parent = new int[_spots.Length];
        for (int i = 0; i < _parent.Length; i++) _parent[i] = i;
        if (!askDirect) _apart.Add((Player, Target));
        _search = Search();
    }

    internal IReadOnlyList<Leg>? Plan { get; private set; }
    /// <summary>The plan ends at the reached spot nearest the target's height, not at the target.</summary>
    internal bool Partial { get; private set; }
    internal bool Finished { get; private set; }
    internal int Asked { get; private set; }
    internal Question? Pending { get; private set; }
    internal int LinkCount => _links.Count;

    internal Spot SpotOf(int id) => _spots[id];
    internal static int LowId(int link) => 2 + 2 * link;
    internal static int HighId(int link) => 3 + 2 * link;

    /// <summary>The question to answer next, or null once the search is over.</summary>
    internal Question? Next()
    {
        if (Pending != null) return Pending;
        if (Finished) return null;
        // Resuming consumes the last answer first, so a plan it completes is kept even at the budget.
        if (!_search.MoveNext() || Asked >= _budget)
        {
            Finished = true;
            return null;
        }
        Pending = _search.Current;
        return Pending;
    }

    /// <summary>Answers the pending question; resolved spots replace endpoints that had to be moved onto the navmesh.</summary>
    internal void Answer(bool connected, Spot? from = null, Spot? to = null)
    {
        if (Pending is not { } q) return;
        Pending = null;
        Asked++;
        if (from is { } f && q.From != Player) _spots[q.From] = f;
        if (to is { } t && q.To != Player) _spots[q.To] = t;
        _answer = connected;
        if (connected) _parent[Find(q.From)] = Find(q.To);
        else _apart.Add((q.From, q.To));
    }

    /// <summary>1 connected, 0 not connected, -1 unknown, from answers so far.</summary>
    internal int Known(int a, int b)
    {
        int ra = Find(a), rb = Find(b);
        if (ra == rb) return 1;
        foreach (var (x, y) in _apart)
        {
            int rx = Find(x), ry = Find(y);
            if ((rx == ra && ry == rb) || (rx == rb && ry == ra)) return 0;
        }
        return -1;
    }

    /// <summary>Which leg the player is on: past a ladder standing at its far end, back again at its near end.</summary>
    internal static int Progress(IReadOnlyList<Leg> plan, int current, Spot player, float reachM, float levelM)
    {
        while (current < plan.Count - 1 && AtEnd(player, plan[current].Exit, plan[current].To, reachM, levelM)) current++;
        while (current > 0 && AtEnd(player, plan[current - 1].To, plan[current - 1].Exit, reachM, levelM)) current--;
        return current;
    }

    private static bool AtEnd(Spot p, Spot end, Spot other, float reachM, float levelM)
        => MathF.Abs(p.Y - end.Y) < MathF.Min(levelM, MathF.Abs(p.Y - other.Y)) && Flat(p, end) <= reachM;

    private IEnumerator<Question> Search()
    {
        int direct = Known(Player, Target);
        if (direct < 0) { yield return new Question(Player, Target); direct = _answer ? 1 : 0; }
        if (direct == 1)
        {
            Plan = new[] { Final() };
            yield break;
        }

        var reachTarget = new List<(int link, bool up)>();
        foreach (var (i, up) in Ordered(Target, Player, -1))
        {
            // Leave half the budget for two-ladder routes once some ladder is known to reach the target.
            if (reachTarget.Count > 0 && Asked >= _budget / 2) break;
            int exit = ExitId(i, up), entry = EntryId(i, up);
            int k = Known(exit, Target);
            if (k < 0) { yield return new Question(exit, Target); k = _answer ? 1 : 0; }
            if (k == 0) continue;
            reachTarget.Add((i, up));
            k = Known(Player, entry);
            if (k < 0) { yield return new Question(Player, entry); k = _answer ? 1 : 0; }
            if (k == 1)
            {
                Plan = new[] { LegOf(i, up), Final() };
                yield break;
            }
        }

        foreach (var (a, aUp) in reachTarget)
        {
            int aEntry = EntryId(a, aUp);
            foreach (var (b, bUp) in Ordered(aEntry, Player, a))
            {
                int exit = ExitId(b, bUp), entry = EntryId(b, bUp);
                int k = Known(exit, aEntry);
                if (k < 0) { yield return new Question(exit, aEntry); k = _answer ? 1 : 0; }
                if (k == 0) continue;
                k = Known(Player, entry);
                if (k < 0) { yield return new Question(Player, entry); k = _answer ? 1 : 0; }
                if (k == 1)
                {
                    Plan = new[] { LegOf(b, bUp), LegOf(a, aUp), Final() };
                    yield break;
                }
            }
        }

        foreach (var q in Closest()) yield return q;
    }

    /// <summary>No route reaches the target: take ladders outward from the player, breadth first, and keep
    /// the reached spot nearest the target's height as a partial plan. A climbable ledge, a jump or a
    /// breakable obstacle the navmesh does not model may close the rest.</summary>
    private IEnumerable<Question> Closest()
    {
        var reached = new List<int> { Player };
        var via = new Dictionary<int, (int from, int link, bool up)>();
        var taken = new HashSet<(int link, bool up)>();
        int best = Player;
        while (true)
        {
            // Take every ladder whose foot the answers so far connect to a reached spot.
            for (bool grew = true; grew;)
            {
                grew = false;
                for (int i = 0; i < _links.Count; i++)
                    foreach (bool up in Directions)
                    {
                        int entry = EntryId(i, up), exit = ExitId(i, up);
                        int at = taken.Contains((i, up)) ? -1 : reached.FindIndex(r => Known(r, entry) == 1);
                        if (at < 0) continue;
                        taken.Add((i, up));
                        if (reached.Exists(r => Known(r, exit) == 1)) continue;
                        reached.Add(exit);
                        via[exit] = (reached[at], i, up);
                        grew = true;
                        if (Nearer(exit, best)) best = exit;
                    }
            }
            var hop = BlockedHop(reached, taken, best);
            if (hop is { } h) SetPartial(h.from, via, h);
            else if (best != Player) SetPartial(best, via);

            // Next question: a near ladder foot whose top is near the target.
            Question? next = null;
            float cost = float.MaxValue;
            foreach (int r in reached)
                for (int i = 0; i < _links.Count; i++)
                    foreach (bool up in Directions)
                    {
                        int entry = EntryId(i, up);
                        if (taken.Contains((i, up)) || Known(r, entry) >= 0) continue;
                        float c = Distance(_spots[r], _spots[entry]) + Distance(_spots[ExitId(i, up)], _spots[Target]);
                        if (c < cost) { cost = c; next = new Question(r, entry); }
                    }
            if (next is not { } q) yield break;
            yield return q;
        }
    }

    /// <summary>Within the target's neighbourhood: a level nearer the target's height, or the same level horizontally
    /// nearer; never the player's own level. Ladders farther out may still be crossed on the way.</summary>
    private bool Nearer(int spot, int best)
    {
        if (Flat(_spots[spot], _spots[Target]) > NeighbourhoodM) return false;
        float gap = Gap(spot), bestGap = Gap(best);
        if (best == Player) return gap <= bestGap - LevelM;
        if (gap < bestGap - LevelM) return true;
        return MathF.Abs(gap - bestGap) < LevelM && Flat(_spots[spot], _spots[Target]) < Flat(_spots[best], _spots[Target]);
    }

    /// <summary>One ladder past an unmodelled obstacle: its foot level with and near a reached spot the answers say
    /// does not connect, and its top at least a level nearer the target than the best connected end.</summary>
    private (int from, int link, bool up)? BlockedHop(List<int> reached, HashSet<(int link, bool up)> taken, int best)
    {
        (int from, int link, bool up)? hop = null;
        int hopExit = best;
        foreach (int r in reached)
            for (int i = 0; i < _links.Count; i++)
                foreach (bool up in Directions)
                {
                    int entry = EntryId(i, up), exit = ExitId(i, up);
                    if (taken.Contains((i, up)) || Known(r, entry) != 0) continue;
                    if (MathF.Abs(_spots[r].Y - _spots[entry].Y) >= LevelM || Flat(_spots[r], _spots[entry]) > BlockedHopM) continue;
                    if (Flat(_spots[exit], _spots[Target]) > NeighbourhoodM || Gap(exit) > Gap(hopExit) - LevelM) continue;
                    hop = (r, i, up);
                    hopExit = exit;
                }
        return hop;
    }

    /// <summary>The ladders from the player to <paramref name="end"/>, then a blocked ladder if any, then a last leg
    /// that stops at the top of the last ladder.</summary>
    private void SetPartial(int end, Dictionary<int, (int from, int link, bool up)> via, (int from, int link, bool up)? blocked = null)
    {
        var legs = new List<Leg>();
        int stop = end;
        if (blocked is { } b)
        {
            var leg = LegOf(b.link, b.up);
            legs.Add(leg with { Blocked = true });
            stop = ExitId(b.link, b.up);
        }
        for (int at = end; at != Player && via.TryGetValue(at, out var step); at = step.from)
            legs.Insert(0, LegOf(step.link, step.up));
        legs.Add(new Leg(_spots[stop], _spots[stop], -1, false));
        Plan = legs;
        Partial = true;
    }

    private float Gap(int id) => MathF.Abs(_spots[id].Y - _spots[Target].Y);

    /// <summary>Both directions of every ladder, cheapest straight-line detour from start to goal first.</summary>
    private List<(int link, bool up)> Ordered(int goal, int start, int skip)
    {
        var order = new List<(int link, bool up, float cost)>();
        for (int i = 0; i < _links.Count; i++)
        {
            if (i == skip) continue;
            foreach (bool up in Directions)
            {
                Spot entry = _spots[EntryId(i, up)], exit = _spots[ExitId(i, up)];
                order.Add((i, up, Distance(_spots[start], entry) + Distance(entry, exit) + Distance(exit, _spots[goal])));
            }
        }
        order.Sort((x, y) => x.cost.CompareTo(y.cost));
        return order.ConvertAll(o => (o.link, o.up));
    }

    private Leg LegOf(int link, bool up) => new(_spots[EntryId(link, up)], _spots[ExitId(link, up)], link, up);
    private Leg Final() => new(_spots[Target], _spots[Target], -1, false);
    private static int EntryId(int link, bool up) => up ? LowId(link) : HighId(link);
    private static int ExitId(int link, bool up) => up ? HighId(link) : LowId(link);

    private int Find(int i)
    {
        while (_parent[i] != i) i = _parent[i] = _parent[_parent[i]];
        return i;
    }

    internal static float Distance(Spot a, Spot b)
        => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));

    internal static float Flat(Spot a, Spot b)
        => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
