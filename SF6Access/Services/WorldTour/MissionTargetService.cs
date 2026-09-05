using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Where the current World Tour mission wants you to go.
///
/// <para>The game tracks this in <c>app.worldtour.WTMissionSystem</c>:
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
    // objective is a person far more often than not.
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
        public Target(ManagedObject go, float x, float y, float z)
        {
            Go = go; X = x; Y = y; Z = z; Ok = go != null;
        }
    }

    private static bool _loggedOnce;

    /// <summary>Locate the current mission objective. Re-resolved every call —
    /// never cached, because the mission, the target and the object behind it all
    /// change underneath us.</summary>
    public static Target Find()
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

        foreach (string getter in TargetListGetters)
        {
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

                    if (!_loggedOnce)
                    {
                        _loggedOnce = true;
                        API.LogInfo($"[SF6Access] Mission target found via {getter} " +
                                    $"(mission {missionId}) — the beacon has something to point at");
                    }
                    return new Target(go, p.x, p.y, p.z);
                }
            }
        }
        return default;
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
