using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Where the current World Tour mission wants you to go.
///
/// <para><b>The HUD marker answers first.</b> The mission the PLAYER selected
/// (primary or secondary) is the one the game's own on-screen arrow follows, and
/// that arrow reads its target out of <c>app.UICityHud_MissionGuide</c> — see
/// <see cref="MissionGuideReader"/>. Whenever that yields a target we can take a
/// position from, it wins, because it is the player's choice rather than the
/// story's.</para>
///
/// <para>The mission system below stays as the fallback for everything the HUD
/// cannot answer: the marker not built yet, hidden, or following nothing.</para>
///
/// <para>The fallback tracks this in <c>app.worldtour.WTMissionSystem</c>:
/// <c>FindProgressMissionId()</c> gives the mission the HUD is following, and
/// <c>GetList{Npc,Om,Zone}MissionTargetInfo(id)</c> return that mission's target
/// records. Each record carries <c>ListHolderObj</c> — a list of LIVE scene
/// <c>GameObject</c>s — so the objective is not an abstract coordinate but a real
/// object we can read a transform from, and point a sound at.</para>
///
/// <para>All three target kinds are asked in turn because a mission objective is
/// sometimes a person, sometimes a thing, sometimes a place, and the game keeps
/// them in separate lists.</para>
///
/// <para><b>Empty is normal, not an error:</b> the holder list is empty whenever
/// the target has not streamed into the loaded scene — a different district, or
/// simply not spawned yet. That is a "no beacon right now", never a failure.</para>
/// </summary>
public static class MissionTargetService
{
    private const string MISSION_SYSTEM = "app.worldtour.WTMissionSystem";
    private const string FIND_PROGRESS_ID = "FindProgressMissionId";

    // The three target kinds, in the order they are asked. NPC first: a mission
    // objective is a person far more often than not — which is also the order of
    // the game's own app.UICityHud_MissionGuide.eTargetType (NPC, OM, ZONE), so
    // the index doubles as that enum's value when naming the kind in the log.
    private static readonly string[] TargetListGetters =
    {
        "GetListNpcMissionTargetInfo",
        "GetListOmMissionTargetInfo",
        "GetListZoneMissionTargetInfo",
    };

    /// <summary>The objective's GameObject and its position, or ok=false when
    /// there is nothing to point at right now.</summary>
    public readonly struct Target
    {
        public readonly ManagedObject Go;
        public readonly float X, Y, Z;
        public readonly bool Ok;
        /// <summary>Which objective this is, independent of the object holding it: the game swaps a far NPC
        /// for its map icon (<c>IconNpc_*</c>, 1.8 m higher) and back as the player moves (2026-09-26).</summary>
        public readonly string Slot;
        public Target(ManagedObject go, float x, float y, float z, string slot)
        {
            Go = go; X = x; Y = y; Z = z; Ok = go != null; Slot = slot;
        }
    }

    // What the last logged fix was. The log line is worth having on every CHANGE
    // of objective and worthless once a second for the same one.
    private static string _lastLogKey;

    /// <summary>Locate the current mission objective. Re-resolved every call —
    /// never cached, because the mission, the target and the object behind it all
    /// change underneath us.</summary>
    public static Target Find()
    {
        var hud = FindFromGuide();
        return hud.Ok ? hud : FindFromMissionSystem();
    }

    /// <summary>The objective the game's own HUD marker is following — the
    /// player's selected mission. Not-ok whenever the marker has nothing, or its
    /// target has no readable position yet.</summary>
    private static Target FindFromGuide()
    {
        var guide = MissionGuideReader.Read();
        if (!guide.Ok) return default;

        var p = PositionOf(guide.TargetObject);
        if (!p.ok) return default;

        float x = p.x, y = p.y, z = p.z;

        // WHERE THE PLAYER MUST STAND beats where the object happens to be. For a
        // street NPC the two are nearly the same point; for one on an upper floor
        // the object's own origin is somewhere the player can never be, and the
        // navmesh says so. Refused answers fall straight back to the transform, so
        // this can only improve the destination.
        var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (me.ok && AccessPointReader.TryRead(guide.AccessTarget, guide.TargetObject,
                                               me.x, me.y, me.z, p.x, p.y, p.z,
                                               out float ax, out float ay, out float az))
        {
            x = ax; y = ay; z = az;
        }

        LogFix("hud", guide.MissionId, MissionGuideReader.TargetTypeName(guide.TargetType),
               guide.TargetObject, x, y, z);
        return new Target(guide.TargetObject, x, y, z, $"hud:{guide.MissionId}:{guide.TargetType}");
    }

    private static Target FindFromMissionSystem()
    {
        var sys = API.GetManagedSingleton(MISSION_SYSTEM) as ManagedObject;
        if (sys == null) return default;

        object idBoxed;
        try { idBoxed = FlowHelper.Call(sys, FIND_PROGRESS_ID); }
        catch { return default; }
        if (idBoxed == null) return default;

        uint missionId;
        try { missionId = System.Convert.ToUInt32(idBoxed); }
        catch { return default; }

        for (int kind = 0; kind < TargetListGetters.Length; kind++)
        {
            string getter = TargetListGetters[kind];
            var list = FlowHelper.Call(sys, getter, missionId) as ManagedObject;
            int n = FlowHelper.GetListCount(list);
            for (int i = 0; i < n; i++)
            {
                var info = FlowHelper.GetListItem(list, i);
                if (info == null) continue;
                // The game's own "this record actually has a target" flag.
                if (!FlowHelper.ReadBoolField(info, "HaveMissionTarget")
                    && FlowHelper.Call(info, "get_HaveMissionTarget") is bool have && !have) continue;

                // Getter first: ListHolderObj is a property with no backing field,
                // and asking for the field logs a "Member not found" line on every
                // pass — this runs once a second, all session.
                var holders = FlowHelper.Call(info, "get_ListHolderObj") as ManagedObject
                              ?? AvatarFieldReader.GetProp(info, "ListHolderObj");
                int h = FlowHelper.GetListCount(holders);
                for (int j = 0; j < h; j++)
                {
                    var go = FlowHelper.GetListItem(holders, j);
                    var p = PositionOf(go);
                    if (!p.ok) continue;

                    if (LogFix("system", missionId, MissionGuideReader.TargetTypeName(kind), go, p.x, p.y, p.z))
                        LogHolders(sys, missionId);
                    return new Target(go, p.x, p.y, p.z, $"system:{missionId}:{kind}:{i}");
                }
            }
        }
        return default;
    }

    /// <summary>One log line per CHANGE of objective — a new mission, a new
    /// target kind, a different object, or a switch between the HUD marker and
    /// the mission system. The position is a snapshot at that moment, not a
    /// tracked value: an objective that walks does not deserve a line a second.
    /// </summary>
    private static bool LogFix(string source, uint missionId, string type,
                               ManagedObject go, float x, float y, float z)
    {
        string key = $"{source}|{missionId}|{type}|{FlowHelper.AddressOf(go):X}";
        if (key == _lastLogKey) return false;
        _lastLogKey = key;
        API.LogInfo($"[SF6Access] Mission guide: id={missionId} type={type} source={source} " +
                    $"name={FlowHelper.Call(go, "get_Name") as string ?? "?"} pos=({x:0.0}, {y:0.0}, {z:0.0})");
        return true;
    }

    /// <summary>Every live target object of the mission, by name and position, and why the HUD
    /// marker was not used: the first holder is not necessarily the one the player must reach.</summary>
    private static void LogHolders(ManagedObject sys, uint missionId)
    {
        var line = new System.Text.StringBuilder($"[SF6Access] Mission guide: HUD unused ({MissionGuideReader.LastMiss ?? "hit"})");
        for (int kind = 0; kind < TargetListGetters.Length; kind++)
        {
            var list = FlowHelper.Call(sys, TargetListGetters[kind], missionId) as ManagedObject;
            int n = FlowHelper.GetListCount(list);
            for (int i = 0; i < n; i++)
            {
                var info = FlowHelper.GetListItem(list, i);
                var holders = FlowHelper.Call(info, "get_ListHolderObj") as ManagedObject
                              ?? AvatarFieldReader.GetProp(info, "ListHolderObj");
                int h = FlowHelper.GetListCount(holders);
                line.Append($"; {MissionGuideReader.TargetTypeName(kind)}[{i}] holders={h}");
                for (int j = 0; j < h; j++)
                {
                    var go = FlowHelper.GetListItem(holders, j);
                    var p = PositionOf(go);
                    line.Append($" {FlowHelper.Call(go, "get_Name") as string ?? "?"}@({p.x:0.0}, {p.y:0.0}, {p.z:0.0})");
                }
            }
        }
        API.LogInfo(line.ToString());
    }

    private static (float x, float y, float z, bool ok) PositionOf(ManagedObject go)
    {
        try
        {
            var tr = FlowHelper.Call(go, "get_Transform") as ManagedObject;
            var p = FlowHelper.Call(tr, "get_Position");
            if (p == null) return (0f, 0f, 0f, false);
            float x = FlowHelper.ReadVecComponent(p, "x");
            float y = FlowHelper.ReadVecComponent(p, "y");
            float z = FlowHelper.ReadVecComponent(p, "z");
            // An exact origin means the read failed, not an objective at (0,0,0).
            return (x, y, z, float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z)
                             && (x != 0f || y != 0f || z != 0f));
        }
        catch { return (0f, 0f, 0f, false); }
    }
}
