using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// What the game's own World Tour HUD marker is following — i.e. the mission the
/// PLAYER has selected, primary or secondary.
///
/// <para><b>Why not the mission system.</b>
/// <c>WTMissionSystem.FindProgressMissionId()</c> answers "the mission in
/// progress", which is the story's idea of the current objective and not
/// necessarily the one the player picked in the mission list. The on-screen
/// arrow does not use it directly: <c>app.UICityHud_MissionGuide</c> builds its
/// own candidate list (<c>GetTargetNpc/OM/Zone</c>), picks one with
/// <c>GetNeareastTarget</c>/<c>ChangeMissionTarget</c>, and parks it in the
/// <c>missionTarget</c> field. Reading that field is therefore reading the very
/// thing a sighted player sees the arrow pointing at.</para>
///
/// <para>The guide is a scene component, found the same way
/// <see cref="FieldHeadingService"/> finds the minimap window: ask the current
/// scene for components of the type. The INSTANCE is never cached — the HUD is
/// rebuilt across loads and a stale pointer would be read as truth — only the
/// TDB lookups, which are the expensive part, are.</para>
///
/// <para><b>Absent is normal:</b> outside the World Tour city (menus, battles,
/// loading) the component simply is not in the scene, and no target is not a
/// failure. A genuine BIND failure — the type or a member missing, e.g. after a
/// game patch renames something — is warned about once, naming the member, so it
/// can be told apart from "nothing selected right now" in the log.</para>
/// </summary>
public static class MissionGuideReader
{
    private const string GUIDE_TYPE = "app.UICityHud_MissionGuide";
    private const string INFO_TYPE = GUIDE_TYPE + ".ProgressMisionInfo";
    private const string TARGET_TYPE_ENUM = GUIDE_TYPE + ".eTargetType";
    private const string SCENE_MANAGER = "via.SceneManager";
    private const string SCENE_TYPE = "via.Scene";
    private const string FIND_COMPONENTS = "findComponents(System.Type)";

    // Members read on the guide component and on its ProgressMisionInfo record.
    private const string F_MISSION_TARGET = "missionTarget";
    private const string F_CUR_MISSION_ID = "curProgressMissionId";
    private const string F_TARGET_OBJECT = "TargetObject";

    /// <summary><c>ProgressMisionInfo.AccessTarget</c>
    /// (<c>UICityHud_MissionGuide.cs:195</c>) — the thing that knows WHERE THE PLAYER
    /// MUST STAND to interact with this objective. See <see cref="AccessPointReader"/>
    /// for why that, and not the target's own transform, is the place to walk to.</summary>
    private const string F_ACCESS_TARGET = "AccessTarget";
    private const string F_TARGET_TYPE = "TargetType";

    /// <summary>Unreadable target kind. Not an <c>eTargetType</c> value: the
    /// game's enum starts at 0 (NPC), so a negative marks "not read".</summary>
    public const int TYPE_UNKNOWN = -1;

    /// <summary>What the HUD marker is currently following. <see cref="Ok"/> is
    /// false when there is no guide, no selection, or the selected target has
    /// not streamed into the scene.</summary>
    public readonly struct Guide
    {
        public readonly ManagedObject TargetObject;

        /// <summary>The objective's access target, or null. Never held across
        /// frames by anyone — read it, use it, drop it.</summary>
        public readonly ManagedObject AccessTarget;
        public readonly uint MissionId;
        /// <summary>app.UICityHud_MissionGuide.eTargetType, or <see cref="TYPE_UNKNOWN"/>.</summary>
        public readonly int TargetType;
        public bool Ok => TargetObject != null;

        public Guide(ManagedObject targetObject, uint missionId, int targetType, ManagedObject accessTarget)
        {
            TargetObject = targetObject; MissionId = missionId; TargetType = targetType;
            AccessTarget = accessTarget;
        }
    }

    private static TypeDefinition _guideTd;
    private static Method _findComponents;
    private static bool _bindChecked;
    private static bool _bindOk;

    /// <summary>Why the last <see cref="Read"/> found nothing, for the fallback's log line; null after a hit.</summary>
    public static string LastMiss { get; private set; }

    /// <summary>The HUD marker's current target. Re-resolved on every call.</summary>
    public static Guide Read()
    {
        try
        {
            LastMiss = "guide type not bound";
            if (!EnsureBound()) return default;

            LastMiss = "no HUD mission guide in the scene";
            var guide = FindGuide();
            if (guide == null) return default;

            var info = AvatarFieldReader.GetProp(guide, F_MISSION_TARGET);
            LastMiss = "guide has no missionTarget; targetList count=" +
                       FlowHelper.GetListCount(AvatarFieldReader.GetProp(guide, "targetList") as ManagedObject);
            if (info == null) return default;

            var go = AvatarFieldReader.GetProp(info, F_TARGET_OBJECT);
            LastMiss = "missionTarget has no TargetObject";
            if (go == null) return default;
            LastMiss = null;

            uint id = FlowHelper.ReadUIntField(guide, F_CUR_MISSION_ID);
            // The access target rides along: it is optional, and a mission whose
            // objective publishes none simply keeps the old behaviour.
            var access = AvatarFieldReader.GetProp(info, F_ACCESS_TARGET) as ManagedObject;
            return new Guide(go, id, ReadTargetType(info), access);
        }
        catch { return default; }
    }

    /// <summary>The game's own name for an <c>eTargetType</c> value (NPC / OM /
    /// ZONE), read from the TDB so the log never shows an invented label. "?"
    /// when the value is outside the enum.</summary>
    public static string TargetTypeName(int value)
        => value < 0 ? "?" : FlowHelper.ResolveEnumName(TARGET_TYPE_ENUM, value) ?? "?";

    /// <summary>eTargetType carries no byte base in the game's own declaration
    /// (unlike the byte enums elsewhere in the same dump), so it is a 32-bit
    /// read. Getter fallback covers the case where the property has no backing
    /// field.</summary>
    private static int ReadTargetType(ManagedObject info)
    {
        int t = FlowHelper.ReadIntField(info, F_TARGET_TYPE, TYPE_UNKNOWN);
        if (t != TYPE_UNKNOWN) return t;
        var boxed = FlowHelper.Call(info, "get_" + F_TARGET_TYPE);
        if (boxed == null) return TYPE_UNKNOWN;
        try { return System.Convert.ToInt32(boxed); } catch { return TYPE_UNKNOWN; }
    }

    /// <summary>One-time shape check of the guide type and its record: warns
    /// exactly once, naming the member that is missing, and then stays quiet.
    /// Needs no live instance — the TDB knows the shape from the moment it is up
    /// — so "nothing on screen" never looks like a bind failure.</summary>
    private static bool EnsureBound()
    {
        if (_bindChecked) return _bindOk;
        _bindChecked = true;

        _guideTd = TDB.Get().FindType(GUIDE_TYPE);
        if (_guideTd == null)
        {
            API.LogWarning($"[SF6Access] Mission guide: type {GUIDE_TYPE} not found; using WTMissionSystem only");
            return false;
        }

        string missing = FirstMissing(_guideTd, F_MISSION_TARGET)
                         ?? FirstMissing(_guideTd, F_CUR_MISSION_ID);

        var infoTd = TDB.Get().FindType(INFO_TYPE);
        if (infoTd == null) missing ??= INFO_TYPE;
        else missing ??= FirstMissing(infoTd, F_TARGET_OBJECT) ?? FirstMissing(infoTd, F_TARGET_TYPE);

        _findComponents = TDB.Get().FindType(SCENE_TYPE)?.GetMethod(FIND_COMPONENTS);
        if (_findComponents == null) missing ??= SCENE_TYPE + "." + FIND_COMPONENTS;

        if (missing != null)
        {
            API.LogWarning($"[SF6Access] Mission guide: cannot bind '{missing}' on {GUIDE_TYPE}; using WTMissionSystem only");
            return false;
        }

        _bindOk = true;
        return true;
    }

    /// <summary>The member name when the type exposes it neither as a field (nor
    /// as an auto-property backing field) nor as a <c>get_</c> accessor; null
    /// when it is reachable.</summary>
    private static string FirstMissing(TypeDefinition td, string name)
    {
        try
        {
            if (td.GetField(name) != null) return null;
            if (td.GetField($"<{name}>k__BackingField") != null) return null;
            if (td.GetMethod("get_" + name) != null) return null;
        }
        catch { }
        return name;
    }

    /// <summary>The live guide component, or null when the HUD is not in the
    /// scene (menus, battles, loading).</summary>
    private static ManagedObject FindGuide()
    {
        var runtimeType = _guideTd?.GetRuntimeType();
        if (runtimeType == null || _findComponents == null) return null;

        var sceneMgr = API.GetNativeSingleton(SCENE_MANAGER);
        var scene = (sceneMgr as IObject)?.Call("get_CurrentScene") as IObject;
        if (scene == null) return null;

        var list = _findComponents.InvokeBoxed(typeof(object), scene, new object[] { runtimeType }) as ManagedObject;
        return FlowHelper.GetListCount(list) > 0 ? FlowHelper.GetListItem(list, 0) : null;
    }
}
