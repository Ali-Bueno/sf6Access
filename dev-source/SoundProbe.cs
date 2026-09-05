// SF6Access — Wwise sound probe (DEV TOOL, not part of the shipped mod).
//
// Purpose: find out whether SF6's own audio system can emit a spatialized
// "beacon" at an NPC's position, and which trigger id sounds right. Wwise only
// plays events that already live in the game's soundbanks, and the trigger ids
// are hashed uints with no enum anywhere in the type database, so the usable
// ids can only be discovered at RUNTIME by walking a sound container.
//
// How it is loaded: this is a REFramework.NET *source plugin*. Copy it to
//     <game>\reframework\plugins\source\SoundProbe.cs
// and REFramework compiles it in-process on save — edit, save, and it
// hot-reloads without restarting SF6. It is deliberately SELF-CONTAINED (no
// reference to SF6Access.dll) so it compiles on its own, and it registers no
// hooks that the shipped mod already owns, so both can be loaded at once.
//
// Keys (game window must be foreground). F2/F3/F4 because F10 opens the Windows
// window menu, F12 is Steam's screenshot key, and F7/F8/F9 are the mod's dumps:
//   F2         scan the nearest NPC: log every component on its GameObject and
//              enumerate its sound container's trigger ids
//   F3         fire the next trigger id
//   Shift+F3   fire the previous one
//   Ctrl+F3    jump 10 ids at a time (the confirmed container exposes ~543)
//   F4         re-fire the current one (turn the camera between presses to hear
//              whether the sound is really positioned at the NPC)
//   Shift+F4   stop the last fired trigger — some triggers are LOOPS and will
//              otherwise play forever (this once forced a game restart)
//   Ctrl+F4    stop every trigger fired this session (leaves the game's own
//              audio alone — the panic button to reach for while auditioning)
//   Ctrl+Shift+F4  last resort: stopAll(), silences the music too, and it does
//              not come back on its own
//   F1         list every GameObject within a few metres of the player, nearest
//              first, with its name and its app-level components. Answers "what
//              IS that thing" without guessing a type name — stand on the thing
//              you care about and it tops the list.
//   F5         next filter · Shift+F5 previous. The filters are
//              All → Voices → Effects → then EACH SOUNDBANK on its own. It
//              speaks the filter name and how many triggers it holds.
//
// Auditioning one bank at a time is the only practical way through ~500 sounds,
// because the bank name already says whether it is worth hearing. Measured on a
// World Tour NPC (464 triggers in 15 banks): the voice banks are the three with
// IsLanguage=True (`esf002_v_es` and friends, the actor's own lines); the useful
// noises are `foot_steps_es` and `wcs_mvmt_cotton_01_es` (cloth); and the ones
// to avoid are `dmg_cmn_es` / `dmg_human_es` / `down_human_es` (pain and falls)
// and `ui_bh_raid_cmn_es` (UI, so probably authored 2D).
//
// While stepping, the soundbank name is spoken whenever the group changes — the
// hashed trigger ids have no readable name, but each group
// (soundlib.SoundTriggerInfoListData) exposes Bank.ResourcePath and its own
// IsLanguage flag, which is what makes hundreds of sounds navigable at all.
//
// SCOPE: this file is now RESEARCH ONLY — scan an NPC, audition its sounds,
// stop a runaway loop. The shipped feature it produced lives in the mod, as
// Hooks/WorldTour/FieldBeaconHooks.cs + Services/WorldTour/NpcBeaconService.cs.
// A beacon prototype used to live here too and was deliberately removed: the
// mod's version is gated on dialogue and interaction range, and running both at
// once would double every ping and reintroduce the collisions with World Tour
// dialogue that those gates exist to prevent.
//
// Two dead ends worth not repeating, both measured in game 2026-08-03:
//  - A "spy" mode polling soundlib.SoundManager's request lists to watch what
//    the game plays by itself. Those lists drain within the frame, so a
//    per-frame poll caught ~1% of requests and never once an NPC. Doing this
//    properly needs a hook on SoundManager.postRequestInfo, from the compiled
//    DLL rather than here (there is no documented unhook, and this file
//    hot-reloads).
//  - Boosting a beacon over the music via RequestInfo.AttenuationScalingFactor.
//    It was made to work end to end and verified by reading the value back, and
//    at 2.0 it was still inaudible in A/B. See docs/sf6-architecture.md.
//
// Nothing managed is cached across frames (project stale-param rule): only the
// discovered trigger ids — plain uints — survive between presses, and the NPC
// and its container are re-resolved on every fire, so the probe always targets
// whoever is nearest right now and can never hold a dead pointer.
//
// API basis (decompiled stubs, verified names):
//   soundlib.SoundContainer.trigger(System.UInt32)        — play by trigger id
//   soundlib.SoundContainer.AllTriggerInfoListData        — IList<SoundTriggerInfoListData>
//   soundlib.SoundTriggerInfoListData.TriggerInfoList     — IList<SoundTriggerInfo>
//   soundlib.SoundTriggerInfo.TriggerId / .EventId        — uint
//   app.sound.SoundContainerApp : soundlib.SoundContainer
//
// The emitter is matched by INHERITANCE, never by name. Confirmed in game
// 2026-08-03: a World Tour NPC's own emitter is
//   app.sound.SoundDynamicContainerApp : SoundContainerApp : soundlib.SoundContainer
// and "SoundContainer" is NOT a substring of "SoundDynamicContainerApp", so a
// name filter misses it. The same NPC also carries app.sound.SoundNPCBehavior,
// app.sound.SoundMotionSequence and app.sound.SoundRequestReferenceTableContainer
// — the last one merely ends in "Container" and has no trigger(), which is
// exactly the kind of false positive the inheritance check rejects.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;

namespace SF6AccessDev
{
    public class SoundProbe
    {
        private const string AVATAR_MANAGER = "app.worldtour.avatar.AvatarManager";
        // Declaring type of the by-id play method; the NPC's component is the
        // derived app.sound.SoundContainerApp, so resolve the method here.
        private const string SOUND_CONTAINER = "soundlib.SoundContainer";
        private const string TRIGGER_BY_ID = "trigger(System.UInt32)";
        private const string STOP_TRIGGERED = "stopTriggered(System.UInt32, via.GameObject, System.UInt32)";
        private const string SOUND_MANAGER = "soundlib.SoundManager";
        // Wwise game parameters live here; setting one is via.simplewwise
        // .SendRequest.setRtpcValue(ulong gameObjId, uint rtpcId, float value,
        // bool isGlobal, float durationMs, bool bypassInterpolation).
        private const string BANK_INFO_MANAGER = "via.simplewwise.BankInfoManager";
        private const int MAX_RTPCS_LOGGED = 200;
        // The concrete emitter types to sweep for. findComponents needs a real
        // runtime type, so the abstract soundlib.SoundContainer cannot be used.
        private static readonly string[] CONTAINER_TYPES =
        {
            "app.sound.SoundContainerApp",
            "app.sound.SoundDynamicContainerApp",
        };
        private const int MAX_CONTAINERS_LOGGED = 60;
        private const string STOP_ALL = "stopAll()";

        private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11;
        // F5 for the voice filter: F6 is the mod's stats dump, F7/F8/F9 its other
        // dumps, F10 the Windows window menu and F12 Steam's screenshot key.
        private const int VK_F1 = 0x70, VK_F2 = 0x71, VK_F3 = 0x72, VK_F4 = 0x73, VK_F5 = 0x74;

        private const string SCENE_TYPE = "via.Scene";
        private const string FIND_COMPONENTS = "findComponents(System.Type)";
        // Enumerating by via.Transform sweeps the WHOLE scene, because every
        // GameObject has one. Guessing type names failed for the tutorial's
        // step-on pads (ScenarioZoneGroup / MissionZoneTarget /
        // WTZoneAccessTargetSimple all returned 0 while the pads were visibly on
        // the ground), so the reliable question is not "where is type X" but
        // "what is near me".
        private const string TRANSFORM_TYPE = "via.Transform";
        private const float NEARBY_RANGE_M = 12f;
        // The raw sweep found ~450-500 objects within range, so this cap hides
        // almost all of them: it only ever shows what the player is standing on.
        private const int MAX_NEARBY_LOGGED = 30;

        // What the raw sweep turned up next to the tutorial pads: numbered
        // 'vi_020000_NN' objects carrying this controller AND their own
        // SoundContainerApp. docs/worldtour-accessibility-plan.md already had it
        // as the interactive-object controller, with onContact(CollisionInfo)
        // and SelfState — a contact event and a state is exactly the shape a
        // step-on pad needs. Scanning for the type directly avoids the cap above.
        private const string GIMMICK_TYPE = "app.worldtour.om.GimmickVisualController";
        private const int MAX_GIMMICKS_LOGGED = 40;
        // The member named in the plan doc; read per instance so that stepping
        // on a pad can be seen as a state change rather than guessed at.
        private const string GIMMICK_STATE = "SelfState";
        private const int MAX_MEMBERS_LOGGED = 60;
        // Watch cadence: ~1.5 s at the 60 Hz this callback runs at. Slow enough
        // that the spoken distance does not stutter while walking, fast enough to
        // catch the frame a pad changes state on.
        private const int WATCH_TICKS = 90;
        // Guide cadence, the two-speed shape the NPC beacon already uses: the cue
        // tightens as the player closes in, so "getting warmer" is audible without
        // any spoken distance at all. The scan runs on this same counter, which is
        // why it doubles as the watch period.
        private const int GUIDE_NEAR_TICKS = 45;
        private const int GUIDE_FAR_TICKS = 120;
        private const float GUIDE_NEAR_M = 4f;

        // The tutorial pad family, read off the objects themselves: the six
        // 'vi_020000[_NN]' gimmicks, whose own soundbank is 'om020000_es' — the
        // same 020000 id. The two other gimmick families in the same scene
        // (vi_031xxx with ForceChain/WorkRate, vi_017xxx bare) are different props
        // and must not be guided to. A name prefix is good enough for the probe;
        // the shipped version needs a rule that survives other tutorials.
        private const string PAD_NAME_PREFIX = "vi_020000";
        // Which of om020000_es's three sounds the guide uses when F3 has not been
        // touched: the second one, picked by ear by the tester (2026-08-14,
        // id 1581634986). F3 still overrides it live.
        private const int PREFERRED_SOUND_INDEX = 1;

        // Distance under which every scan logs the target pad's exact distance, so
        // the log shows whether a pad was actually walked onto.
        private const float PAD_LOG_NEAR_M = 3f;
        // Sampling period inside that range (~0.25 s). Stepping on a pad is an
        // instant; at the 0.75 s used further out the moment can fall between two
        // samples and read as "nothing changed".
        private const int PAD_CLOSE_TICKS = 15;
        // Horizontal distance under which the player counts as standing on a pad.
        // MEASURED, not guessed: the two pads confirmed stepped on in game bottomed
        // out at 0.41 m and 0.54 m in 3D, and since a pad's origin sits a fixed
        // height off the player's, those are roughly 0 m and 0.35 m on the ground
        // plane. 0.6 m clears both with margin.
        private const float PAD_CLEAR_FLAT_M = 0.6f;
        private const string ALL_DONE = "All pads done";
        // Pads already walked over, by name. Cleared when the guide is toggled, so
        // a wrong call costs one press of F1 twice rather than a restart.
        private static readonly HashSet<string> _cleared = new HashSet<string>();
        // Engine-level flags worth watching: a cleared pad most plausibly stops
        // being drawn, stops updating, or gets disabled, and none of those live in
        // a field that a type dump would show.
        private static readonly string[] GO_FLAGS = { "DrawSelf", "UpdateSelf", "Valid" };
        private static readonly string[] COMPONENT_FLAGS = { "Enabled", "DrawSelf", "UpdateSelf" };

        private static bool _watch;
        private static int _watchTicks;
        private static int _watchPeriod = WATCH_TICKS;
        // Ticks since the last guide cue, counted independently of the sampling
        // period so the two cadences cannot drag each other around.
        private static int _sinceCue;
        // Where the scanned ids came from, so F4 fires them on the same KIND of
        // emitter. A flag, re-resolved every press — never a cached ManagedObject.
        private static bool _fromGimmick;
        // Previous snapshot: pad name -> (member -> value).
        private static readonly Dictionary<string, Dictionary<string, string>> _watchState =
            new Dictionary<string, Dictionary<string, string>>();
        private static string _lastSpoken;
        // Components to name an object by. via.* entries are engine plumbing
        // (Transform, colliders, motion) and say nothing about what a thing IS.
        private const string ENGINE_PREFIX = "via.";
        private const int MAX_COMPONENT_NAMES = 8;
        // Ctrl jump size — the container confirmed on a World Tour NPC exposes
        // ~543 ids, far too many to audition one press at a time.
        private const int COARSE_STEP = 10;

        private static readonly List<uint> _ids = new List<uint>();
        // Parallel to _ids: this trigger is a VOICE line, and which soundbank
        // group it came from.
        private static readonly List<bool> _voice = new List<bool>();
        private static readonly List<int> _group = new List<int>();
        // Per group: short bank name and the game's own IsLanguage flag.
        private static readonly List<string> _groupLabel = new List<string>();
        private static readonly List<bool> _groupIsLang = new List<bool>();

        // Positions into _ids that F3 walks, under the current filter.
        private static readonly List<int> _view = new List<int>();
        // Filter selector. 0 = every trigger, 1 = voices only, 2 = effects only
        // (footsteps, cloth, props — these matter because filler NPCs have no
        // spoken lines and their noises are the only thing that can locate
        // them), and 3+i = the i-th soundbank on its own. Auditioning one bank
        // at a time is the only practical way through ~500 sounds: the bank name
        // already says whether it is worth hearing (foot_steps_es yes,
        // dmg_human_es no).
        private static int _filter;
        private const int FIXED_FILTERS = 3;
        private static readonly string[] FIXED_FILTER_NAMES = { "All", "Voices", "Effects" };
        // Last group announced while stepping, so the bank is spoken only when
        // it actually changes.
        private static int _lastSpokenGroup = -1;
        // Last id fired, so a looping trigger can be stopped again — plus every
        // id fired this session, so "stop mine" can silence an accidental loop
        // without nuking the game's music the way stopAll() does.
        private static uint _lastFiredId;
        private static readonly List<uint> _firedIds = new List<uint>();

        private static int _cursor = -1;
        private static bool _f1, _f2, _f3, _f4, _f5;
        // Type FullName of the container the ids came from — a plain string, so
        // it survives between frames where a ManagedObject must not.
        private static string _containerType;

        [PluginEntryPoint]
        public static void Initialize()
        {
            Tolk_Load();
            API.LogInfo("[SoundProbe] loaded — F1 pad watch (Shift raw nearby, Ctrl dump nearest), " +
                        "F2 scan NPC (Ctrl scan nearest gimmick), F3 next (Shift prev, Ctrl x10), " +
                        "F4 fire (Shift stop last, Ctrl stop mine, Ctrl+Shift stop all), F5 filter (Shift back)");
            Say("Sound probe loaded");
        }

        [PluginExitPoint]
        public static void Shutdown()
        {
            // Hot-reload teardown: drop the discovered ids, never unload Tolk —
            // the shipped mod owns that handle and is still running.
            _ids.Clear(); _voice.Clear(); _group.Clear(); _view.Clear();
            _groupLabel.Clear(); _groupIsLang.Clear();
            _cursor = -1; _lastSpokenGroup = -1; _containerType = null;
            _watch = false; _watchTicks = 0; _watchPeriod = WATCH_TICKS; _sinceCue = 0; _cleared.Clear();
            _watchState.Clear(); _lastSpoken = null; _fromGimmick = false;
            API.LogInfo("[SoundProbe] unloaded");
        }

        [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
        public static void Tick()
        {
            if (Edge(VK_F1, ref _f1))
            {
                if (Down(VK_CONTROL)) DumpNearestGimmick();
                else if (Down(VK_SHIFT)) NearbyScan();
                else ToggleWatch();
            }
            if (_watch) WatchTick();
            if (Edge(VK_F2, ref _f2))
            {
                if (Down(VK_CONTROL)) ScanGimmick();
                else if (Down(VK_SHIFT)) ListContainers();
                else Scan();
            }
            if (Edge(VK_F3, ref _f3))
            {
                int step = Down(VK_CONTROL) ? COARSE_STEP : 1;
                Step(Down(VK_SHIFT) ? -step : step);
            }
            if (Edge(VK_F4, ref _f4))
            {
                if (Down(VK_CONTROL) && Down(VK_SHIFT)) StopEverything();
                else if (Down(VK_CONTROL)) StopMine();
                else if (Down(VK_SHIFT)) StopLast();
                else Fire();
            }
            if (Edge(VK_F5, ref _f5))
            {
                int n = FIXED_FILTERS + _groupLabel.Count;
                _filter = ((_filter + (Down(VK_SHIFT) ? -1 : 1)) % n + n) % n;
                RebuildView();
            }
        }

        private static string FilterName(int f)
            => f < FIXED_FILTERS ? FIXED_FILTER_NAMES[f] : _groupLabel[f - FIXED_FILTERS];

        /// <summary>Recompute what F3 walks after a scan or a filter change.</summary>
        private static void RebuildView()
        {
            _view.Clear();
            for (int i = 0; i < _ids.Count; i++)
            {
                bool keep = _filter switch
                {
                    1 => _voice[i],
                    2 => !_voice[i],
                    < FIXED_FILTERS => true,
                    _ => _group[i] == _filter - FIXED_FILTERS,
                };
                if (keep) _view.Add(i);
            }
            _cursor = -1;
            _lastSpokenGroup = -1;
            API.LogInfo($"[SoundProbe] filter = {FilterName(_filter)}, {_view.Count} of {_ids.Count}");
            Say($"{FilterName(_filter)}, {_view.Count}");
        }

        /// <summary>List the trigger ids of the nearest NPC's sound container.
        /// Every component on that GameObject is logged too — that dump is the
        /// point of the first press, since we do not yet know which component
        /// carries the audio on a World Tour NPC.</summary>
        private static void Scan()
        {
            var npc = NearestOther();
            if (npc == null) { Say("No NPC found"); return; }
            _fromGimmick = false;
            ScanContainerOn(Call(npc, "get_GameObject") as ManagedObject, "nearest NPC");
        }

        /// <summary>The same audition scan aimed at the nearest gimmick. A pad
        /// carries its OWN SoundContainerApp, so its own sounds are the ones that
        /// will not sound like a mod talking over the game.</summary>
        private static void ScanGimmick()
        {
            var found = Gimmicks();
            if (found == null || found.Count == 0) { Say("No gimmicks"); return; }
            DumpTriggerOverloads();
            DumpRtpcs();
            // Prefer a pad over whatever gimmick happens to be closest, so the
            // ids collected are the ones the guide will actually fire.
            int p = NearestPadIndex(found);
            var pick = found[p < 0 ? 0 : p];
            _fromGimmick = true;
            ScanContainerOn(pick.go, $"gimmick '{pick.name}'");
        }

        /// <summary>Every trigger/stop overload on the container type. Whether a
        /// positioned overload exists — one taking the GameObject to play AT —
        /// decides the whole cue design: with it, any known sound can be placed
        /// on a pad; without it, only sounds already in the pad's own banks can.</summary>
        private static void DumpTriggerOverloads()
        {
            var td = TDB.Get().FindType(SOUND_CONTAINER);
            var methods = td?.GetMethods();
            if (methods == null) { API.LogInfo($"[SoundProbe] {SOUND_CONTAINER} has no readable methods"); return; }
            API.LogInfo($"[SoundProbe] {SOUND_CONTAINER} trigger/stop overloads:");
            foreach (var m in methods)
            {
                try
                {
                    string name = m.Name ?? "";
                    if (name.IndexOf("trigger", StringComparison.OrdinalIgnoreCase) < 0 &&
                        name.IndexOf("stop", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var ps = m.GetParameters();
                    var sig = new List<string>();
                    if (ps != null)
                        for (int i = 0; i < ps.Count; i++)
                            sig.Add($"{ps[i].Type?.FullName ?? "?"} {ps[i].Name ?? $"p{i}"}");
                    API.LogInfo($"[SoundProbe]   {m.ReturnType?.FullName ?? "void"} {name}({string.Join(", ", sig)})");
                }
                catch { }
            }
        }

        /// <summary>Every sound emitter in the scene, with the soundbanks it
        /// carries.
        ///
        /// <para>The point is to find a bank that is ALWAYS loaded. A cue that
        /// must sound the same everywhere cannot come from a bank tied to one
        /// place: the tutorial panels' <c>om020000_es</c> exists only during that
        /// tutorial, so an id from it is silence anywhere else. UI banks are the
        /// usual answer, since menus exist in every mode — this is how to find
        /// which container holds them and what they are called.</para>
        /// </summary>
        private static void ListContainers()
        {
            var scene = CurrentScene();
            var find = TDB.Get().FindType(SCENE_TYPE)?.GetMethod(FIND_COMPONENTS);
            if (scene == null || find == null) { Say("No scene"); return; }

            int shown = 0;
            foreach (string typeName in CONTAINER_TYPES)
            {
                var rt = TDB.Get().FindType(typeName)?.GetRuntimeType();
                if (rt == null) { API.LogInfo($"[SoundProbe] {typeName} not in this build"); continue; }

                ManagedObject all;
                try { all = find.InvokeBoxed(typeof(object), scene, new object[] { rt }) as ManagedObject; }
                catch (Exception ex) { API.LogError($"[SoundProbe] {typeName} scan failed: {ex.Message}"); continue; }

                int n = Count(all);
                API.LogInfo($"[SoundProbe] {typeName}: {n} in scene");
                for (int i = 0; i < n && shown < MAX_CONTAINERS_LOGGED; i++)
                {
                    var c = Item(all, i);
                    string banks = BankSummary(c);
                    // Emitters with no banks of their own are noise in this list.
                    if (string.IsNullOrEmpty(banks)) continue;
                    shown++;
                    var go = Call(c, "get_GameObject") as ManagedObject;
                    API.LogInfo($"[SoundProbe]   '{Call(go, "get_Name") as string ?? "?"}'  {banks}");
                }
            }
            Say($"{shown} containers");
        }

        /// <summary>The loaded scene, or null. Every scene-wide sweep needs it, so
        /// it lives here rather than being re-derived at each call site.</summary>
        private static IObject CurrentScene()
        {
            try
            {
                var sceneMgr = API.GetNativeSingleton("via.SceneManager");
                return (sceneMgr as IObject)?.Call("get_CurrentScene") as IObject;
            }
            catch { return null; }
        }

        /// <summary>Bank names and trigger counts on one container, as one line.</summary>
        private static string BankSummary(ManagedObject container)
        {
            var groups = Prop(container, "AllTriggerInfoListData");
            int n = Count(groups);
            if (n == 0) return "";
            var parts = new List<string>();
            for (int i = 0; i < n; i++)
            {
                var g = Item(groups, i);
                parts.Add($"{BankName(g)}[{Count(Prop(g, "TriggerInfoList"))}]");
            }
            return string.Join(", ", parts);
        }

        /// <summary>Every RTPC (Wwise "game parameter") the loaded banks declare,
        /// name beside id.
        ///
        /// <para>This is the ONLY route to a pitch change. Wwise has no "set the
        /// pitch of this sound" call — <c>SendRequest.setRtpcValue</c> moves a
        /// game parameter, and pitch shifts only if the game's own Wwise project
        /// wired that parameter to pitch FOR THAT SOUND. So the question is not
        /// "can we call it" but "did Capcom author one", and only this list can
        /// answer that. Names and ids are parallel arrays.</para>
        /// </summary>
        private static void DumpRtpcs()
        {
            var td = TDB.Get().FindType(BANK_INFO_MANAGER);
            if (td == null) { API.LogInfo($"[SoundProbe] {BANK_INFO_MANAGER} not found"); return; }

            var countM = td.GetMethod("getRtpcNameTblCount()");
            var nameM = td.GetMethod("getRtpcNameTbl(System.UInt64)");
            var idM = td.GetMethod("getRtpcIdTbl(System.UInt64)");
            if (countM == null || nameM == null)
            {
                API.LogInfo("[SoundProbe] RTPC name table not reachable");
                return;
            }

            ulong n;
            try { n = Convert.ToUInt64(countM.InvokeBoxed(typeof(object), null, Array.Empty<object>())); }
            catch (Exception ex) { API.LogError($"[SoundProbe] RTPC count failed: {ex.Message}"); return; }

            API.LogInfo($"[SoundProbe] {n} RTPCs declared by the loaded banks:");
            for (ulong i = 0; i < n && i < MAX_RTPCS_LOGGED; i++)
            {
                string name = "?";
                string id = "?";
                try { name = nameM.InvokeBoxed(typeof(object), null, new object[] { i }) as string ?? "?"; } catch { }
                try { id = idM?.InvokeBoxed(typeof(object), null, new object[] { i })?.ToString() ?? "?"; } catch { }
                API.LogInfo($"[SoundProbe]   rtpc[{i}] {name} = {id}");
            }
            if (n > MAX_RTPCS_LOGGED)
                API.LogInfo($"[SoundProbe]   ... {n - MAX_RTPCS_LOGGED} more not listed");
        }

        private static void ScanContainerOn(ManagedObject go, string what)
        {
            _ids.Clear(); _voice.Clear(); _group.Clear(); _view.Clear();
            _groupLabel.Clear(); _groupIsLang.Clear();
            _cursor = -1; _lastSpokenGroup = -1; _containerType = null;

            var comps = Call(go, "get_Components") as ManagedObject;
            int n = Count(comps);
            API.LogInfo($"[SoundProbe] {what} has {n} components:");

            var containers = new List<ManagedObject>();
            for (int i = 0; i < n; i++)
            {
                var c = Item(comps, i);
                var td = c?.GetTypeDefinition();
                string t = td?.GetFullName() ?? "?";
                bool isContainer = IsSoundContainer(td);
                API.LogInfo($"[SoundProbe]   [{i}] {t}{(isContainer ? "   <-- SOUND CONTAINER" : "")}");
                if (isContainer) containers.Add(c);
            }

            if (containers.Count == 0) { Say($"No sound container on {what}"); return; }
            if (TriggerMethod() == null) { Say("Trigger method not found"); return; }

            // Take the first container that actually exposes trigger ids: a World
            // Tour NPC carries several (the confirmed one is the derived
            // app.sound.SoundDynamicContainerApp), and an empty one is useless.
            foreach (var c in containers)
            {
                string t = c.GetTypeDefinition()?.GetFullName() ?? "?";
                int before = _ids.Count;
                CollectTriggerIds(c);
                API.LogInfo($"[SoundProbe] {t} exposed {_ids.Count - before} trigger ids");
                if (_ids.Count > before) { _containerType = t; break; }
            }

            if (_containerType == null)
            {
                Say($"{containers.Count} containers, no trigger ids");
                return;
            }

            int voices = _voice.FindAll(v => v).Count;
            API.LogInfo($"[SoundProbe] {_ids.Count} triggers in {_groupLabel.Count} banks, " +
                        $"{voices} voices / {_ids.Count - voices} effects");
            _filter = 0;
            RebuildView();
        }

        /// <summary>Start or stop the pad watch. Two jobs at once: it says where
        /// the nearest gimmick is, so the pads can be walked onto without being
        /// seen, and it logs every change in the set — which is the one thing
        /// still unknown, since SelfState read 0 on all 16 in every scan taken
        /// so far and none of those was taken with a pad actually stepped on.</summary>
        private static void ToggleWatch()
        {
            _watch = !_watch;
            _watchTicks = 0;
            _watchPeriod = WATCH_TICKS;
            _sinceCue = 0;
            _cleared.Clear();
            _watchState.Clear();
            _lastSpoken = null;
            API.LogInfo($"[SoundProbe] pad watch {(_watch ? "ON" : "OFF")}");
            if (!_watch) { Say("Watch off"); return; }

            // Collect the pad's own sounds here rather than on a separate key:
            // without them the guide is silent, and a silent guide is exactly how
            // the first pad test read as "nothing happened".
            if (!_fromGimmick || _view.Count == 0) ScanGimmick();
            GimmickScan();
        }

        /// <summary>One watch step: re-scan, log what changed, say the nearest.</summary>
        private static void WatchTick()
        {
            if (++_watchTicks < _watchPeriod) return;
            _watchTicks = 0;

            var found = Gimmicks();
            if (found == null) return;

            // Watch the PADS ONLY, but watch EVERYTHING about them. One named
            // field was guessed at twice and moved neither time, so this stops
            // guessing: it snapshots every readable value on every component and
            // reports whichever one moves. Non-pad gimmicks are skipped because
            // they never change and would bury the signal.
            var now = new Dictionary<string, Dictionary<string, string>>();
            foreach (var g in found)
            {
                if (!g.name.StartsWith(PAD_NAME_PREFIX, StringComparison.Ordinal)) continue;
                now[g.name] = Snapshot(g.go);
            }

            foreach (var kv in now)
            {
                if (!_watchState.TryGetValue(kv.Key, out var was))
                {
                    API.LogInfo($"[SoundProbe] watch: '{kv.Key}' APPEARED with {kv.Value.Count} watched values");
                    continue;
                }
                foreach (var m in kv.Value)
                    if (was.TryGetValue(m.Key, out string old) && old != m.Value)
                        API.LogInfo($"[SoundProbe] watch: '{kv.Key}' {m.Key}: {old} -> {m.Value}");
            }
            foreach (var kv in _watchState)
                if (!now.ContainsKey(kv.Key))
                    API.LogInfo($"[SoundProbe] watch: '{kv.Key}' GONE");

            _watchState.Clear();
            foreach (var kv in now) _watchState[kv.Key] = kv.Value;

            if (found.Count == 0) { _watchPeriod = WATCH_TICKS; return; }

            // Guide to the nearest PAD, not to the nearest gimmick of any family:
            // the props in this same scene sit 8-18 m away and would drag the
            // player off the tutorial.
            // Clear whatever the player is standing on FIRST, so the same tick can
            // hand the guide over to the next pad rather than sounding a pad that
            // has already been walked over.
            ClearPadsUnderfoot(found);

            int p = NearestPadIndex(found, skipCleared: true);
            if (p < 0)
            {
                // Every pad walked. Say so once and fall quiet — a guide that
                // never ends is worse than no guide.
                _watchPeriod = WATCH_TICKS;
                if (_lastSpoken != ALL_DONE) { _lastSpoken = ALL_DONE; Say(ALL_DONE); }
                return;
            }
            var target = found[p];

            // Sampling and cue rate are separate concerns. Standing on a pad is an
            // instant, so the SAMPLING tightens hard up close to catch it; the CUE
            // must not, or it would turn into a machine-gun exactly where the
            // player needs to hear the game's own confirmation.
            _watchPeriod = target.dist <= PAD_LOG_NEAR_M ? PAD_CLOSE_TICKS
                         : target.dist <= GUIDE_NEAR_M ? GUIDE_NEAR_TICKS
                         : GUIDE_FAR_TICKS;

            // Log the approach itself, with the horizontal distance beside the 3D
            // one: the two together are what calibrate PAD_CLEAR_FLAT_M honestly
            // instead of by guesswork.
            if (target.dist <= PAD_LOG_NEAR_M)
                API.LogInfo($"[SoundProbe] approach: '{target.name}' at {target.dist:0.00}m " +
                            $"(flat {target.flat:0.00}m)");

            _sinceCue += _watchPeriod;
            int cueEvery = target.dist <= GUIDE_NEAR_M ? GUIDE_NEAR_TICKS : GUIDE_FAR_TICKS;
            if (_sinceCue >= cueEvery) { _sinceCue = 0; GuideCue(target.go); }

            // Only the tail number is spoken: the full 'vi_020000_10' read out
            // every couple of seconds is unusable, and the tail is what tells the
            // pads of one set apart.
            string line = $"{Tail(target.name)}, {target.dist:0} metres";
            if (line == _lastSpoken) return;
            _lastSpoken = line;
            Say(line);
        }

        /// <summary>Mark as cleared every pad the player is standing on, silence
        /// it, and count it off.
        ///
        /// <para><b>Why proximity and not a game flag.</b> With two pads confirmed
        /// stepped on, all 46 readable values on each pad — the GameObject's
        /// flags and every field of its 9 components — were IDENTICAL before and
        /// after, and no pad left the scene. The pad object simply does not record
        /// being used; that state lives somewhere in the mission system. So the
        /// clear is inferred from where the player is, which is measurable and
        /// needs nothing from the game.</para>
        /// </summary>
        private static void ClearPadsUnderfoot(
            List<(float dist, float flat, ManagedObject comp, ManagedObject go, string name)> found)
        {
            foreach (var g in found)
            {
                if (!g.name.StartsWith(PAD_NAME_PREFIX, StringComparison.Ordinal)) continue;
                if (g.flat > PAD_CLEAR_FLAT_M || _cleared.Contains(g.name)) continue;

                _cleared.Add(g.name);
                // Silence this one now: the cue is fired repeatedly on the pad's
                // own emitter, so without an explicit stop the last one keeps
                // ringing from a pad the player has already dealt with.
                StopOn(g.go, _lastFiredId);
                API.LogInfo($"[SoundProbe] cleared '{g.name}' at flat {g.flat:0.00}m " +
                            $"({_cleared.Count} of {PadCount(found)})");
                _lastSpoken = null;   // let the next target be announced immediately
            }
        }

        private static int PadCount(
            List<(float dist, float flat, ManagedObject comp, ManagedObject go, string name)> found)
        {
            int n = 0;
            foreach (var g in found)
                if (g.name.StartsWith(PAD_NAME_PREFIX, StringComparison.Ordinal)) n++;
            return n;
        }

        /// <summary>Stop one trigger on one specific object's emitter.</summary>
        private static void StopOn(ManagedObject go, uint id)
        {
            if (id == 0) return;
            var container = ContainerOn(go);
            var stop = TDB.Get().FindType(SOUND_CONTAINER)?.GetMethod(STOP_TRIGGERED);
            if (container == null || stop == null) return;
            // duration 0 = stop now rather than fade out.
            try { stop.InvokeBoxed(typeof(object), container, new object[] { id, go, 0u }); }
            catch (Exception ex) { API.LogError($"[SoundProbe] stop on pad failed: {ex.Message}"); }
        }

        /// <summary>Everything readable about one pad, as name -> value pairs:
        /// the GameObject's own flags, then for every component its enable flags
        /// and every non-static field whose value is a plain value or string.
        ///
        /// <para>Fields holding objects are skipped: their ToString is a constant
        /// ("REFrameworkNET.ManagedObject") and would never differ, so they add
        /// noise without adding signal.</para>
        /// </summary>
        private static Dictionary<string, string> Snapshot(ManagedObject go)
        {
            var snap = new Dictionary<string, string>();
            if (go == null) return snap;

            foreach (string flag in GO_FLAGS) snap[$"go.{flag}"] = Member(go, flag);

            var comps = Call(go, "get_Components") as ManagedObject;
            int n = Count(comps);
            for (int i = 0; i < n; i++)
            {
                var c = Item(comps, i);
                var td = c?.GetTypeDefinition();
                if (td == null) continue;
                // Short type name keeps the log line readable; the index keeps two
                // components of the same type apart.
                string full = td.GetFullName() ?? "?";
                int dot = full.LastIndexOf('.');
                string label = $"[{i}]{(dot >= 0 ? full.Substring(dot + 1) : full)}";

                foreach (string flag in COMPONENT_FLAGS) snap[$"{label}.{flag}"] = Member(c, flag);

                try
                {
                    var fields = td.GetFields();
                    if (fields == null) continue;
                    foreach (var f in fields)
                    {
                        try
                        {
                            if (f.IsStatic()) continue;
                            var v = f.GetDataBoxed(typeof(object), c.GetAddress(), false);
                            if (v == null || v is ManagedObject) continue;
                            snap[$"{label}.{f.Name}"] = v.ToString();
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return snap;
        }

        /// <summary>Sound the pad through its OWN emitter, so the cue arrives in
        /// 3D from the pad itself with no vec3 maths. Which of its sounds is used
        /// is whatever F3 has selected, so the three in its bank can be compared
        /// live without restarting the guide.</summary>
        private static void GuideCue(ManagedObject padGo)
        {
            if (!_fromGimmick || _view.Count == 0) return;
            var container = ContainerOn(padGo);
            var trigger = TriggerMethod();
            if (container == null || trigger == null) return;

            int sel = _cursor >= 0 ? _cursor
                    : PREFERRED_SOUND_INDEX < _view.Count ? PREFERRED_SOUND_INDEX : 0;
            uint id = _ids[_view[sel]];
            if (Call(container, "exists", id) is bool known && !known) return;
            try
            {
                trigger.InvokeBoxed(typeof(object), container, new object[] { id });
                _lastFiredId = id;
                if (!_firedIds.Contains(id)) _firedIds.Add(id);
                // Logged because a cue is otherwise unobservable from the log: a
                // silent failure and a cue the player did not notice look the
                // same, and telling them apart has already cost a test round.
                API.LogInfo($"[SoundProbe] cue id={id} on '{Call(padGo, "get_Name") as string ?? "?"}'");
            }
            catch (Exception ex) { API.LogError($"[SoundProbe] guide cue failed: {ex.Message}"); }
        }

        /// <summary>Trailing numeric segment of an object name, or the whole name
        /// when it has none.</summary>
        private static string Tail(string name)
        {
            if (string.IsNullOrEmpty(name)) return "?";
            int u = name.LastIndexOf('_');
            if (u < 0 || u == name.Length - 1) return name;
            string tail = name.Substring(u + 1);
            foreach (char c in tail) if (!char.IsDigit(c)) return name;
            return tail;
        }

        /// <summary>Every GimmickVisualController in the scene, nearest first,
        /// with its state. Scanning for the type answers the question the raw
        /// sweep cannot: how MANY of these are around and where the others are,
        /// which is what tells a set of tutorial pads apart from one prop that
        /// happened to be underfoot.</summary>
        private static void GimmickScan()
        {
            var found = Gimmicks();
            if (found == null) { Say("No scene"); return; }

            API.LogInfo($"[SoundProbe] gimmick scan — {found.Count} {GIMMICK_TYPE} in scene");
            for (int i = 0; i < found.Count && i < MAX_GIMMICKS_LOGGED; i++)
                API.LogInfo($"[SoundProbe]   {found[i].dist:0.0}m '{found[i].name}' " +
                            $"{GIMMICK_STATE}={Member(found[i].comp, GIMMICK_STATE)}  [{Components(found[i].go)}]");
            if (found.Count > MAX_GIMMICKS_LOGGED)
                API.LogInfo($"[SoundProbe]   ... {found.Count - MAX_GIMMICKS_LOGGED} more not listed");

            Say(found.Count == 0 ? "No gimmicks" : $"{found.Count} gimmicks, nearest {found[0].dist:0.0} metres");
        }

        /// <summary>Full member list of the nearest gimmick. Which field means
        /// "already stepped on" cannot be guessed from the type name, and the
        /// answer decides whether a sequential guide is possible at all.</summary>
        private static void DumpNearestGimmick()
        {
            var found = Gimmicks();
            if (found == null || found.Count == 0) { Say("No gimmicks"); return; }

            var g = found[0];
            var td = g.comp?.GetTypeDefinition();
            API.LogInfo($"[SoundProbe] nearest gimmick '{g.name}' at {g.dist:0.0}m — {td?.GetFullName()}");
            try
            {
                var fields = td?.GetFields();
                if (fields != null)
                    foreach (var f in fields)
                    {
                        string v;
                        // typeof(object) boxes at the field's DECLARED width; asking
                        // for int here would grab adjacent bytes on a byte enum.
                        try { v = f.GetDataBoxed(typeof(object), g.comp.GetAddress(), f.IsStatic())?.ToString() ?? "null"; }
                        catch { v = "(read error)"; }
                        API.LogInfo($"[SoundProbe]   field {f.Type?.GetFullName()} {f.Name} = {v}");
                    }
            }
            catch { }
            try
            {
                var methods = td?.GetMethods();
                int shown = 0, total = methods?.Count ?? 0;
                if (methods != null)
                    foreach (var m in methods)
                    {
                        if (shown++ >= MAX_MEMBERS_LOGGED) break;
                        API.LogInfo($"[SoundProbe]   method {m.Name}");
                    }
                if (total > MAX_MEMBERS_LOGGED)
                    API.LogInfo($"[SoundProbe]   ... {total - MAX_MEMBERS_LOGGED} more methods not listed");
            }
            catch { }

            // The two members that are objects rather than values. SettingData is
            // the authored config for this gimmick, and SuccessFsmCondition is
            // named like the "this one is done" test — either could be what marks
            // a pad as already stepped on.
            var setting = Prop(g.comp, "SettingData");
            var std = setting?.GetTypeDefinition();
            API.LogInfo($"[SoundProbe]   SettingData = {std?.GetFullName() ?? "null"}");
            try
            {
                var sfields = std?.GetFields();
                if (sfields != null)
                    foreach (var f in sfields)
                    {
                        string v;
                        try { v = f.GetDataBoxed(typeof(object), setting.GetAddress(), f.IsStatic())?.ToString() ?? "null"; }
                        catch { v = "(read error)"; }
                        API.LogInfo($"[SoundProbe]     {f.Type?.GetFullName()} {f.Name} = {v}");
                    }
            }
            catch { }

            var cond = Prop(g.comp, "SuccessFsmCondition");
            int cn = Count(cond);
            API.LogInfo($"[SoundProbe]   SuccessFsmCondition = {cn} entries");
            for (int i = 0; i < cn; i++)
                API.LogInfo($"[SoundProbe]     [{i}] {Call(cond, "get_Item", i) as string ?? "?"}");

            Say($"Dumped {g.name}");
        }

        /// <summary>The scene's gimmicks with their distance to the player,
        /// nearest first. Null means the scene or the player was unreadable —
        /// distinct from an empty list, which means there are none.</summary>
        private static List<(float dist, float flat, ManagedObject comp, ManagedObject go, string name)> Gimmicks()
        {
            var sceneMgr = API.GetNativeSingleton("via.SceneManager");
            var scene = (sceneMgr as IObject)?.Call("get_CurrentScene") as IObject;
            var find = TDB.Get().FindType(SCENE_TYPE)?.GetMethod(FIND_COMPONENTS);
            var rt = TDB.Get().FindType(GIMMICK_TYPE)?.GetRuntimeType();
            if (scene == null || find == null || rt == null) return null;

            var p = PlayerPos();
            if (!p.ok) return null;

            var all = find.InvokeBoxed(typeof(object), scene, new object[] { rt }) as ManagedObject;
            int n = Count(all);

            var list = new List<(float dist, float flat, ManagedObject comp, ManagedObject go, string name)>();
            for (int i = 0; i < n; i++)
            {
                var comp = Item(all, i);
                var go = Call(comp, "get_GameObject") as ManagedObject;
                var q = Pos(comp);
                float dx = q.x - p.x, dy = q.y - p.y, dz = q.z - p.z;
                // An unreadable position sorts last rather than vanishing: a
                // gimmick that exists but cannot be placed is still a finding.
                float d = q.ok ? (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) : float.MaxValue;
                // Horizontal distance as well, because "am I standing on it" is a
                // question about the ground plane. A pad's origin sits a fixed
                // height off the player's, which is why the 3D distance bottomed
                // out at 0.41 m and never reached zero on a pad confirmed stepped.
                float f = q.ok ? (float)Math.Sqrt(dx * dx + dz * dz) : float.MaxValue;
                list.Add((d, f, comp, go, Call(go, "get_Name") as string ?? "?"));
            }
            list.Sort((a, b) => a.dist.CompareTo(b.dist));
            return list;
        }

        /// <summary>One member of an object as text, for research logging: the
        /// getter first, then the field under its several possible names.</summary>
        private static string Member(ManagedObject o, string name)
        {
            if (o == null) return "?";
            try { var v = Call(o, "get_" + name); if (v != null) return v.ToString(); } catch { }
            try
            {
                var td = o.GetTypeDefinition();
                var f = td?.GetField($"<{name}>k__BackingField") ?? td?.GetField(name) ?? td?.GetField("_" + name);
                if (f != null) return f.GetDataBoxed(typeof(object), o.GetAddress(), f.IsStatic())?.ToString() ?? "null";
            }
            catch { }
            return "?";
        }

        /// <summary>List everything standing near the player: every GameObject
        /// within a few metres, nearest first, with its name and its non-engine
        /// components. This answers "what IS that thing" without having to guess
        /// a type name first — stand on one of the tutorial's projected pads,
        /// press F1, and it will be at the top of the list.</summary>
        private static void NearbyScan()
        {
            var sceneMgr = API.GetNativeSingleton("via.SceneManager");
            var scene = (sceneMgr as IObject)?.Call("get_CurrentScene") as IObject;
            var find = TDB.Get().FindType(SCENE_TYPE)?.GetMethod(FIND_COMPONENTS);
            var rt = TDB.Get().FindType(TRANSFORM_TYPE)?.GetRuntimeType();
            if (scene == null || find == null || rt == null) { Say("No scene"); return; }

            var p = PlayerPos();
            if (!p.ok) { Say("No player position"); return; }

            var all = find.InvokeBoxed(typeof(object), scene, new object[] { rt }) as ManagedObject;
            int n = Count(all);

            var near = new List<(float dist, ManagedObject go, string name)>();
            for (int i = 0; i < n; i++)
            {
                var tr = Item(all, i);
                var pos = Call(tr, "get_Position");
                if (pos == null) continue;
                float x = Vec(pos, "x"), y = Vec(pos, "y"), z = Vec(pos, "z");
                // An exact origin means the read failed, not an object at (0,0,0).
                if (x == 0f && y == 0f && z == 0f) continue;

                float dx = x - p.x, dy = y - p.y, dz = z - p.z;
                float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > NEARBY_RANGE_M) continue;

                var go = Call(tr, "get_GameObject") as ManagedObject;
                near.Add((d, go, Call(go, "get_Name") as string ?? "?"));
            }
            near.Sort((a, b) => a.dist.CompareTo(b.dist));

            API.LogInfo($"[SoundProbe] nearby scan — {n} transforms in scene, " +
                        $"{near.Count} within {NEARBY_RANGE_M}m of the player");
            for (int i = 0; i < near.Count && i < MAX_NEARBY_LOGGED; i++)
                API.LogInfo($"[SoundProbe]   {near[i].dist:0.0}m '{near[i].name}'  [{Components(near[i].go)}]");

            Say(near.Count > 0 ? $"{near.Count} nearby" : "Nothing nearby");
        }

        /// <summary>The app-level components of a GameObject — what identifies it.
        /// via.* components are skipped: every object has a Transform and colliders
        /// and they say nothing about what the thing is.</summary>
        private static string Components(ManagedObject go)
        {
            var comps = Call(go, "get_Components") as ManagedObject;
            int n = Count(comps);
            var names = new List<string>();
            for (int i = 0; i < n && names.Count < MAX_COMPONENT_NAMES; i++)
            {
                string t = Item(comps, i)?.GetTypeDefinition()?.GetFullName();
                if (t == null || t.StartsWith(ENGINE_PREFIX)) continue;
                names.Add(t);
            }
            return names.Count > 0 ? string.Join(", ", names) : "engine only";
        }

        /// <summary>The player avatar's world position.</summary>
        private static (float x, float y, float z, bool ok) PlayerPos()
        {
            var mgr = API.GetManagedSingleton(AVATAR_MANAGER) as ManagedObject;
            if (mgr == null) return (0, 0, 0, false);
            var avatars = Prop(mgr, "AvatarList");
            if (Count(avatars) == 0) avatars = InnerList(avatars);
            int n = Count(avatars);
            for (int i = 0; i < n; i++)
            {
                var av = Item(avatars, i);
                if (av?.GetTypeDefinition()?.GetFullName()?.Contains("AvatarPlayer") != true) continue;
                var p = Pos(av);
                if (p.ok && (p.x != 0f || p.y != 0f || p.z != 0f)) return p;
            }
            return (0, 0, 0, false);
        }

        /// <summary>Match the emitter by INHERITANCE, not by name: the component a
        /// World Tour NPC carries is app.sound.SoundDynamicContainerApp, and
        /// "SoundContainer" is not a substring of "SoundDynamicContainerApp".
        /// Inheritance also correctly rejects app.sound.SoundRequestReferenceTable-
        /// Container, which is a plain Component and has no trigger().</summary>
        private static bool IsSoundContainer(TypeDefinition td)
        {
            if (td == null) return false;
            try { if (td.IsDerivedFrom(SOUND_CONTAINER)) return true; } catch { }
            // Fallback if the derivation query is unavailable for this type.
            try { return td.GetFullName()?.Contains("SoundContainerApp") == true; } catch { return false; }
        }

        /// <summary>Walk AllTriggerInfoListData → TriggerInfoList → TriggerId.
        /// Each group is a soundlib.SoundTriggerInfoListData, which also carries
        /// the game's OWN grouping metadata: <c>IsLanguage</c> (this whole group
        /// is voice data) and <c>Bank</c>, whose <c>ResourcePath</c> names the
        /// soundbank — the closest thing to a human label these hashed ids have,
        /// and what makes auditioning 543 sounds navigable instead of blind.</summary>
        private static void CollectTriggerIds(ManagedObject container)
        {
            var lists = Prop(container, "AllTriggerInfoListData");
            int outer = Count(lists);
            API.LogInfo($"[SoundProbe] AllTriggerInfoListData count = {outer}");
            for (int i = 0; i < outer; i++)
            {
                var group = Item(lists, i);
                bool groupIsLang = ReadBool(group, "IsLanguage");
                string bank = BankName(group);

                int groupIdx = _groupLabel.Count;
                _groupLabel.Add(bank);
                _groupIsLang.Add(groupIsLang);

                var infos = Prop(group, "TriggerInfoList");
                int inner = Count(infos);
                API.LogInfo($"[SoundProbe] group[{groupIdx}] bank={bank} isLanguage={groupIsLang} triggers={inner}");

                for (int j = 0; j < inner; j++)
                {
                    var info = Item(infos, j);
                    uint id = ReadUInt(info, "TriggerId");
                    if (id == 0 || _ids.Contains(id)) continue;

                    // A trigger that carries a PER-LANGUAGE event id is a voice
                    // line: the game has to swap the asset per spoken language.
                    // Footsteps, cloth and prop SE have no localized variant, so
                    // this flag separates the NPC's greetings from its noises
                    // without hardcoding a single id.
                    //
                    // The "not set" value is uint.MaxValue, NOT zero — measured
                    // 2026-08-03 on a World Tour NPC: of 543 triggers, 306 carry
                    // the sentinel in both fields and 237 carry real ids, where
                    // Eng always equals EventId (the base asset) and Jpn differs
                    // from it (the localized variant). Testing against 0 marks
                    // every trigger as a voice.
                    uint jpn = ReadUInt(info, "LanguageEventId_Jpn");
                    uint eng = ReadUInt(info, "LanguageEventId_Eng");
                    bool hasLangIds = (jpn != uint.MaxValue && jpn != 0)
                                      || (eng != uint.MaxValue && eng != 0);
                    // Two independent voice signals — the group's own IsLanguage
                    // flag and the per-trigger language ids. Both are logged so a
                    // session can show which one is the honest discriminator.
                    _ids.Add(id);
                    _voice.Add(groupIsLang || hasLangIds);
                    _group.Add(groupIdx);
                    API.LogInfo($"[SoundProbe]   trigger[{_ids.Count - 1}] id={id} " +
                                $"event={ReadUInt(info, "EventId")} jpn={jpn} eng={eng} " +
                                $"group={groupIdx} langIds={hasLangIds} groupLang={groupIsLang}");
                }
            }
        }

        private static void Step(int delta)
        {
            if (_view.Count == 0) { Say("Nothing scanned"); return; }
            _cursor = ((_cursor + delta) % _view.Count + _view.Count) % _view.Count;

            // Say the index BEFORE playing so the announcement doesn't mask the
            // sound we are judging — plus the bank name, but only when crossing
            // into a different group, so it stays out of the way while sweeping.
            int g = _group[_view[_cursor]];
            if (g != _lastSpokenGroup)
            {
                _lastSpokenGroup = g;
                Say($"{_groupLabel[g]}. {_cursor + 1}");
            }
            else Say($"{_cursor + 1}");

            Fire();
        }

        /// <summary>Play the current id on the nearest NPC's container, resolved
        /// fresh so the sound always comes from whoever is nearest right now.</summary>
        private static void Fire()
        {
            if (_view.Count == 0) { Say("Nothing scanned"); return; }
            // First press right after a scan: start at the first id rather than
            // refusing — nothing has been stepped to yet.
            if (_cursor < 0) { _cursor = 0; Say("1"); }

            var (_, container) = NearestRig();
            var trigger = TriggerMethod();
            if (container == null || trigger == null) { Say("No container in range"); return; }

            uint id = _ids[_view[_cursor]];
            // exists() tells us whether THIS container knows the id — the nearest
            // NPC may have changed since the scan and carry a different bank.
            var known = Call(container, "exists", id);
            if (known is bool ok && !ok)
            {
                API.LogInfo($"[SoundProbe] trigger[{_cursor}] id={id} unknown to this emitter — rescan with F2");
                Say("Not in this emitter");
                return;
            }

            try
            {
                trigger.InvokeBoxed(typeof(object), container, new object[] { id });
                // Remembered so Shift+F4 / Ctrl+F4 can stop it if it turns out to loop.
                _lastFiredId = id;
                if (!_firedIds.Contains(id)) _firedIds.Add(id);
                API.LogInfo($"[SoundProbe] fired trigger[{_cursor}] id={id} bank={_groupLabel[_group[_view[_cursor]]]}");
            }
            catch (Exception ex) { API.LogError($"[SoundProbe] trigger failed: {ex.Message}"); }
        }

        private static Method TriggerMethod()
        {
            try { return TDB.Get().FindType(SOUND_CONTAINER)?.GetMethod(TRIGGER_BY_ID); }
            catch { return null; }
        }

        /// <summary>The nearest non-player avatar's GameObject and its sound
        /// container, matched to the type the ids were scanned from so we fire on
        /// the same kind of emitter; any container will do if that exact type is
        /// not present. The GameObject comes back too because stopTriggered needs
        /// it to identify the emitter.</summary>
        private static (ManagedObject go, ManagedObject container) NearestRig()
        {
            ManagedObject go;
            if (_fromGimmick)
            {
                // Ids scanned off a gimmick are unknown to any NPC, so firing them
                // on the nearest avatar just reports "not in this emitter" and
                // plays nothing — which is exactly how the first pad test failed.
                var found = Gimmicks();
                if (found == null || found.Count == 0) return (null, null);
                int pad = NearestPadIndex(found);
                go = found[pad < 0 ? 0 : pad].go;
            }
            else
            {
                var npc = NearestOther();
                if (npc == null) return (null, null);
                go = Call(npc, "get_GameObject") as ManagedObject;
            }
            return (go, ContainerOn(go));
        }

        /// <summary>The sound emitter on a GameObject: the one matching the type
        /// the ids were scanned from, else any container it carries.</summary>
        private static ManagedObject ContainerOn(ManagedObject go)
        {
            var comps = Call(go, "get_Components") as ManagedObject;
            int n = Count(comps);
            ManagedObject any = null;
            for (int i = 0; i < n; i++)
            {
                var c = Item(comps, i);
                var td = c?.GetTypeDefinition();
                if (!IsSoundContainer(td)) continue;
                if (td.GetFullName() == _containerType) return c;
                any ??= c;
            }
            return any;
        }

        /// <summary>Index of the nearest tutorial pad in a distance-sorted gimmick
        /// list, or -1. The list is sorted, so the first match is the nearest.</summary>
        private static int NearestPadIndex(
            List<(float dist, float flat, ManagedObject comp, ManagedObject go, string name)> found,
            bool skipCleared = false)
        {
            for (int i = 0; i < found.Count; i++)
            {
                if (!found[i].name.StartsWith(PAD_NAME_PREFIX, StringComparison.Ordinal)) continue;
                if (skipCleared && _cleared.Contains(found[i].name)) continue;
                return i;
            }
            return -1;
        }

        // ---- stopping: three levels, least destructive first ----
        //
        // Some triggers are LOOPS; firing one blind leaves the NPC sounding
        // forever, which once forced a game restart. stopAll() does end it, but
        // it also kills the music and the music does not come back on its own —
        // so it is the LAST resort, not the default panic button.

        /// <summary>Stop just the last trigger we fired.</summary>
        private static void StopLast()
        {
            if (_lastFiredId == 0) { Say("Nothing fired yet"); return; }
            Say(StopIds(new List<uint> { _lastFiredId }) ? "Stopped" : "Cannot stop");
        }

        /// <summary>Stop every trigger fired this session — the right panic
        /// button while auditioning, since it leaves the game's own audio alone.</summary>
        private static void StopMine()
        {
            if (_firedIds.Count == 0) { Say("Nothing fired yet"); return; }
            bool ok = StopIds(_firedIds);
            API.LogInfo($"[SoundProbe] stopped {_firedIds.Count} of my triggers (ok={ok})");
            Say(ok ? $"Stopped {_firedIds.Count}" : "Cannot stop");
        }

        /// <summary>Last resort: silences the game entirely, music included.</summary>
        private static void StopEverything()
        {
            try
            {
                TDB.Get().FindType(SOUND_MANAGER)?.GetMethod(STOP_ALL)
                    ?.InvokeBoxed(typeof(object), null, Array.Empty<object>());
                API.LogInfo("[SoundProbe] stopAll() — music included");
                Say("All sound stopped");
            }
            catch (Exception ex) { API.LogError($"[SoundProbe] stopAll failed: {ex.Message}"); }
        }

        private static bool StopIds(List<uint> ids)
        {
            var (go, container) = NearestRig();
            var stop = TDB.Get().FindType(SOUND_CONTAINER)?.GetMethod(STOP_TRIGGERED);
            if (container == null || stop == null) return false;
            foreach (uint id in ids)
            {
                // duration 0 = stop now rather than fade out.
                try { stop.InvokeBoxed(typeof(object), container, new object[] { id, go, 0u }); }
                catch (Exception ex) { API.LogError($"[SoundProbe] stop {id} failed: {ex.Message}"); }
            }
            return true;
        }


        /// <summary>Nearest avatar that is not the player, by 3D distance —
        /// condensed copy of the shipped AvatarFieldReader logic (this file must
        /// stay self-contained). An exact (0,0,0) position means the read failed.</summary>
        private static ManagedObject NearestOther()
        {
            var mgr = API.GetManagedSingleton(AVATAR_MANAGER) as ManagedObject;
            if (mgr == null) return null;

            var avatars = Prop(mgr, "AvatarList");
            if (Count(avatars) == 0) avatars = InnerList(avatars);
            int n = Count(avatars);
            if (n == 0) return null;

            var entries = new List<(ManagedObject av, string type)>(n);
            for (int i = 0; i < n; i++)
            {
                var av = Item(avatars, i);
                if (av != null) entries.Add((av, av.GetTypeDefinition()?.GetFullName() ?? ""));
            }

            float px = 0, py = 0, pz = 0; bool playerOk = false;
            foreach (var (av, type) in entries)
            {
                if (!type.Contains("AvatarPlayer")) continue;
                var p = Pos(av);
                if (p.ok && (p.x != 0f || p.y != 0f || p.z != 0f))
                { px = p.x; py = p.y; pz = p.z; playerOk = true; }
                break;
            }
            if (!playerOk) return null;

            ManagedObject best = null; float bestSqr = float.MaxValue;
            foreach (var (av, type) in entries)
            {
                if (type.Contains("AvatarPlayer")) continue;
                var p = Pos(av);
                if (!p.ok || (p.x == 0f && p.y == 0f && p.z == 0f)) continue;
                float dx = p.x - px, dy = p.y - py, dz = p.z - pz;
                float sqr = dx * dx + dy * dy + dz * dz;
                if (sqr < bestSqr) { bestSqr = sqr; best = av; }
            }
            if (best != null) API.LogInfo($"[SoundProbe] nearest NPC at {Math.Sqrt(bestSqr):0.0} m");
            return best;
        }

        // ---- small engine helpers (mirrors of FlowHelper, kept local on purpose) ----

        private static object Call(ManagedObject o, string m, params object[] a)
        {
            if (o == null) return null;
            try { return (o as IObject)?.Call(m, a); } catch { return null; }
        }

        /// <summary>Object field, trying the plain name, the auto-property
        /// backing field, then the get_ accessor — RE Engine exposes some of
        /// these as plain fields and others as getter-only properties.</summary>
        private static ManagedObject Prop(ManagedObject o, string name)
        {
            if (o == null) return null;
            try { var v = o.GetField(name) as ManagedObject; if (v != null) return v; } catch { }
            try { var v = o.GetField($"<{name}>k__BackingField") as ManagedObject; if (v != null) return v; } catch { }
            return Call(o, "get_" + name) as ManagedObject;
        }

        private static uint ReadUInt(ManagedObject o, string name)
        {
            if (o == null) return 0;
            try
            {
                var td = o.GetTypeDefinition();
                var f = td?.GetField($"<{name}>k__BackingField") ?? td?.GetField(name);
                if (f != null) return Convert.ToUInt32(f.GetDataBoxed(typeof(uint), o.GetAddress(), false));
            }
            catch { }
            try
            {
                var v = Call(o, "get_" + name);
                if (v != null) return Convert.ToUInt32(v);
            }
            catch { }
            return 0;
        }

        private static bool ReadBool(ManagedObject o, string name)
        {
            if (o == null) return false;
            try
            {
                var td = o.GetTypeDefinition();
                var f = td?.GetField($"<{name}>k__BackingField") ?? td?.GetField(name) ?? td?.GetField("_" + name);
                if (f != null) return Convert.ToBoolean(f.GetDataBoxed(typeof(bool), o.GetAddress(), false));
            }
            catch { }
            try { return Call(o, "get_" + name) is bool b && b; } catch { return false; }
        }

        /// <summary>Short, speakable name of a group's soundbank: the file name of
        /// via.ResourceHolder.ResourcePath, without directories or extension.</summary>
        private static string BankName(ManagedObject group)
        {
            try
            {
                var bank = Prop(group, "Bank") ?? Prop(group, "_Bank");
                string path = Call(bank, "get_ResourcePath") as string;
                if (string.IsNullOrEmpty(path)) return "no bank";
                int slash = path.LastIndexOfAny(new[] { '/', '\\' });
                if (slash >= 0) path = path.Substring(slash + 1);
                int dot = path.IndexOf('.');
                if (dot > 0) path = path.Substring(0, dot);
                return path;
            }
            catch { return "no bank"; }
        }

        private static bool IsArray(ManagedObject o)
        {
            try { return o?.GetTypeDefinition()?.FullName?.EndsWith("[]") == true; } catch { return false; }
        }

        private static int Count(ManagedObject list)
        {
            if (list == null) return 0;
            var r = Call(list, IsArray(list) ? "get_Length" : "get_Count");
            try { return r == null ? 0 : Convert.ToInt32(r); } catch { return 0; }
        }

        private static ManagedObject Item(ManagedObject list, int i)
            => list == null ? null : Call(list, IsArray(list) ? "Get" : "get_Item", i) as ManagedObject;

        /// <summary>AvatarList is a SafeList wrapper — fall back to its inner
        /// System.Collections.Generic.List field when the standard count reads 0.</summary>
        private static ManagedObject InnerList(ManagedObject wrapper)
        {
            try
            {
                var fields = wrapper?.GetTypeDefinition()?.GetFields();
                if (fields == null) return wrapper;
                foreach (var f in fields)
                {
                    string ft = f.Type?.GetFullName();
                    if (ft != null && ft.Contains("System.Collections.Generic.List"))
                    {
                        var inner = Prop(wrapper, f.Name);
                        if (inner != null) return inner;
                    }
                }
            }
            catch { }
            return wrapper;
        }

        private static (float x, float y, float z, bool ok) Pos(ManagedObject avatar)
        {
            try
            {
                var tr = Call(Call(avatar, "get_GameObject") as ManagedObject, "get_Transform") as ManagedObject;
                var p = Call(tr, "get_Position");
                if (p == null) return (0, 0, 0, false);
                float x = Vec(p, "x"), y = Vec(p, "y"), z = Vec(p, "z");
                return (x, y, z, float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z));
            }
            catch { return (0, 0, 0, false); }
        }

        /// <summary>via vector components are FIELDS of a value type — the
        /// GetDataBoxed flag is isContainerValueType and must be true here.</summary>
        private static float Vec(object vec, string name)
        {
            try
            {
                if (vec is IObject po)
                {
                    var v = po.Call("get_" + name);
                    if (v != null) return Convert.ToSingle(v);
                }
            }
            catch { }
            try
            {
                if (vec is REFrameworkNET.ValueType vt)
                {
                    var f = vt.GetTypeDefinition()?.GetField(name);
                    if (f != null) return Convert.ToSingle(f.GetDataBoxed(typeof(float), vt.GetAddress(), true));
                }
            }
            catch { }
            return 0f;
        }

        // ---- input + speech (self-contained: no SF6Access reference) ----

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

        // Tolk.dll sits in the game root (the process working directory). The
        // shipped mod already loaded it; Tolk_Load is safe to call again and we
        // must never unload it here.
        [DllImport("Tolk.dll", CharSet = CharSet.Unicode)] private static extern void Tolk_Load();
        [DllImport("Tolk.dll", CharSet = CharSet.Unicode)] private static extern bool Tolk_Output(string str, bool interrupt);

        private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        private static bool Edge(int vk, ref bool last)
        {
            bool now = Down(vk);
            bool fired = now && !last && Foreground();
            last = now;
            return fired;
        }

        private static bool Foreground()
        {
            try
            {
                GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
                return pid == (uint)Environment.ProcessId;
            }
            catch { return false; }
        }

        private static void Say(string text)
        {
            try { Tolk_Output(text, true); } catch (Exception ex) { API.LogError($"[SoundProbe] {ex.Message}"); }
        }
    }
}


