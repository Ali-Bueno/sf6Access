using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The World Tour tutorial's step-on panels: finding them, sounding them, and
/// silencing them.
///
/// <para><b>What they are.</b> Six <c>app.worldtour.om.GimmickVisualController</c>
/// objects named <c>vi_020000[_NN]</c>, each carrying its OWN
/// <c>app.sound.SoundContainerApp</c> whose single bank (<c>om020000_es</c> —
/// same 020000 id as the object) holds three sounds. The cue is therefore one of
/// the panel's own sounds played through the panel's own emitter: positioned in
/// real 3D for free, and part of the game's soundscape rather than a beep over
/// it. Identified in game 2026-08-14; full trail in <c>STATUS.md</c>.</para>
///
/// <para><b>Why "already stepped on" is measured, not read.</b> With two panels
/// confirmed stepped on, all 46 readable values on each — the GameObject's flags
/// and every field of all nine components — were byte-identical before and after,
/// and no panel left the scene. The panel object does not record being used; that
/// state lives in the mission system. So a panel counts as done when the player
/// has stood on it, which needs nothing from the game.</para>
///
/// <para><b>Horizontal distance, not 3D,</b> decides "am I on it": a panel's
/// origin sits a fixed height off the player's, which is why the 3D distance
/// bottomed out at 0.41 m and never neared zero on a panel definitely stood on.</para>
/// </summary>
public static class FieldPadService
{
    private const string SCENE_TYPE = "via.Scene";
    private const string FIND_COMPONENTS = "findComponents(System.Type)";
    private const string GIMMICK_TYPE = "app.worldtour.om.GimmickVisualController";
    private const string SOUND_CONTAINER = "soundlib.SoundContainer";
    private const string TRIGGER_BY_ID = "trigger(System.UInt32)";
    private const string STOP_TRIGGERED = "stopTriggered(System.UInt32, via.GameObject, System.UInt32)";

    // Three gimmick families share the controller type in this scene. Only this
    // one is the tutorial's panels; the others (vi_031xxx with ForceChain/WorkRate,
    // vi_017xxx bare) are props 8-18 m away that would drag the player off course.
    private const string PAD_NAME_PREFIX = "vi_020000";

    // Which of the panel bank's three sounds is the guide cue. Chosen by ear by
    // the tester (2026-08-14). It must NOT be the third: that is the one the game
    // itself plays when a panel is stepped on, and a cue identical to the game's
    // own confirmation would say "you're done" while meaning "come here".
    private const int CUE_SOUND_INDEX = 1;

    // Front/back confusion is the known weak point of 3D audio: a source directly
    // ahead and one directly behind arrive almost identically, so distance and
    // panning alone cannot separate them. The requested fix was a downward pitch
    // shift for "behind", but Wwise exposes no pitch call — pitch only moves
    // through an RTPC that the game's own Wwise project must have wired to that
    // sound. Pending that check, the distinction uses the one thing that is
    // certainly available: the panel's OTHER free sound. Index 2 stays reserved
    // for the game's own step-on confirmation.
    private const int CUE_BEHIND_SOUND_INDEX = 0;

    private static Method _findComponents;
    private static Method _trigger;
    private static Method _stop;
    private static TypeDefinition _gimmickType;

    /// <summary>One panel, with its offset from the player. <see cref="Flat"/> is
    /// the ground-plane distance, <see cref="Dist"/> the full 3D one.</summary>
    public readonly struct Pad
    {
        public readonly ManagedObject Go;
        public readonly string Name;
        public readonly float Dist;
        public readonly float Flat;
        public readonly float Dx;
        public readonly float Dz;
        public Pad(ManagedObject go, string name, float dist, float flat, float dx, float dz)
        {
            Go = go; Name = name; Dist = dist; Flat = flat; Dx = dx; Dz = dz;
        }
    }

    /// <summary>Every tutorial panel in the scene with real distances from the
    /// player, nearest first. Empty when there are none — which is the normal
    /// case everywhere outside that tutorial.</summary>
    public static List<Pad> ReadPads(ManagedObject mgr)
    {
        var result = new List<Pad>();

        var scene = CurrentScene();
        if (scene == null) return result;

        _findComponents ??= TDB.Get().FindType(SCENE_TYPE)?.GetMethod(FIND_COMPONENTS);
        _gimmickType ??= TDB.Get().FindType(GIMMICK_TYPE);
        var runtime = _gimmickType?.GetRuntimeType();
        if (_findComponents == null || runtime == null) return result;

        var player = AvatarFieldReader.ReadPlayerPos(mgr);
        if (!player.ok) return result;

        ManagedObject all;
        try { all = _findComponents.InvokeBoxed(typeof(object), scene, new object[] { runtime }) as ManagedObject; }
        catch { return result; }

        int n = FlowHelper.GetListCount(all);
        for (int i = 0; i < n; i++)
        {
            var comp = FlowHelper.GetListItem(all, i);
            var go = FlowHelper.Call(comp, "get_GameObject") as ManagedObject;
            string name = FlowHelper.Call(go, "get_Name") as string;
            if (name == null || !name.StartsWith(PAD_NAME_PREFIX, System.StringComparison.Ordinal)) continue;

            var p = ReadPos(comp);
            // An exact origin means the read failed, not a panel at (0,0,0).
            if (!p.ok || (p.x == 0f && p.y == 0f && p.z == 0f)) continue;

            float dx = p.x - player.x, dy = p.y - player.y, dz = p.z - player.z;
            float flat = (float)System.Math.Sqrt(dx * dx + dz * dz);
            float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            result.Add(new Pad(go, name, dist, flat, dx, dz));
        }

        result.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        return result;
    }

    /// <summary>Sound one panel through its own emitter. Returns false when it
    /// could not be sounded at all, so a silent failure is distinguishable from a
    /// cue the player did not notice.</summary>
    public static bool Cue(Pad pad, bool behind)
    {
        var container = ContainerOn(pad.Go);
        if (container == null) return false;

        uint id = CueTrigger(container, behind);
        if (id == 0) return false;

        _trigger ??= TDB.Get().FindType(SOUND_CONTAINER)?.GetMethod(TRIGGER_BY_ID);
        if (_trigger == null) return false;

        try
        {
            // trigger() has several overloads, so it is resolved by full signature
            // rather than dispatched by name.
            _trigger.InvokeBoxed(typeof(object), container, new object[] { id });
            return true;
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] Panel cue failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Silence a panel's cue. The cue is fired repeatedly on the panel's
    /// own emitter, so a panel that has just been stepped on would keep ringing
    /// until its last one decayed.</summary>
    public static void Silence(Pad pad)
    {
        var container = ContainerOn(pad.Go);
        _stop ??= TDB.Get().FindType(SOUND_CONTAINER)?.GetMethod(STOP_TRIGGERED);
        if (container == null || _stop == null) return;

        // BOTH cue sounds: the panel may have been sounding as "behind" when the
        // player stepped on it, and stopping only the front one would leave it
        // ringing — which is the exact complaint this is here to fix.
        Stop(container, pad.Go, CueTrigger(container, false));
        Stop(container, pad.Go, CueTrigger(container, true));
    }

    private static void Stop(ManagedObject container, ManagedObject go, uint id)
    {
        if (id == 0) return;
        // duration 0 = stop now rather than fade out.
        try { _stop.InvokeBoxed(typeof(object), container, new object[] { id, go, 0u }); }
        catch (System.Exception ex) { API.LogError($"[SF6Access] Panel silence failed: {ex.Message}"); }
    }

    /// <summary>The cue trigger id, read from the panel's own bank at runtime —
    /// the ids are hashes with no enum anywhere, so they are never hardcoded.</summary>
    private static uint CueTrigger(ManagedObject container, bool behind)
    {
        var groups = AvatarFieldReader.GetProp(container, "AllTriggerInfoListData");
        if (FlowHelper.GetListCount(groups) == 0) return 0;

        var infos = AvatarFieldReader.GetProp(FlowHelper.GetListItem(groups, 0), "TriggerInfoList");
        int n = FlowHelper.GetListCount(infos);
        if (n == 0) return 0;

        int want = behind ? CUE_BEHIND_SOUND_INDEX : CUE_SOUND_INDEX;
        int idx = want < n ? want : 0;
        // ReadUIntField, NOT ReadIntField: trigger ids routinely exceed
        // int.MaxValue, where the int path throws inside Convert and silently
        // yields the fallback.
        return FlowHelper.ReadUIntField(FlowHelper.GetListItem(infos, idx), "TriggerId");
    }

    /// <summary>The panel's sound emitter, matched by INHERITANCE: the component
    /// is app.sound.SoundContainerApp, and "SoundContainer" is not a substring of
    /// it, so a name test misses it.</summary>
    private static ManagedObject ContainerOn(ManagedObject go)
    {
        var comps = FlowHelper.Call(go, "get_Components") as ManagedObject;
        int n = FlowHelper.GetListCount(comps);
        for (int i = 0; i < n; i++)
        {
            var c = FlowHelper.GetListItem(comps, i);
            var td = c?.GetTypeDefinition();
            if (td == null) continue;
            try { if (td.IsDerivedFrom(SOUND_CONTAINER)) return c; } catch { }
        }
        return null;
    }

    private static IObject CurrentScene()
    {
        try
        {
            var sceneMgr = API.GetNativeSingleton("via.SceneManager");
            return (sceneMgr as IObject)?.Call("get_CurrentScene") as IObject;
        }
        catch { return null; }
    }

    private static (float x, float y, float z, bool ok) ReadPos(ManagedObject comp)
    {
        try
        {
            var tr = FlowHelper.Call(FlowHelper.Call(comp, "get_GameObject") as ManagedObject,
                                     "get_Transform") as ManagedObject;
            var p = FlowHelper.Call(tr, "get_Position");
            if (p == null) return (0f, 0f, 0f, false);
            float x = FlowHelper.ReadVecComponent(p, "x");
            float y = FlowHelper.ReadVecComponent(p, "y");
            float z = FlowHelper.ReadVecComponent(p, "z");
            return (x, y, z, float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z));
        }
        catch { return (0f, 0f, 0f, false); }
    }
}
