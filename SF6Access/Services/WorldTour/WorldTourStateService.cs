using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The World Tour field-awareness data layer (WT-1 foundation). Caches the World
/// Tour singletons and exposes the "are we in the field" gate plus the raw
/// city/section/time/weather state that every WT field hook reads.
///
/// <para>The single most important member is <see cref="IsInWorldTour"/>: it is
/// the gate every WT field hook must check first (<c>WTCityManager.IsActivated()</c>),
/// so the readers stay silent in menus, the Battle Hub, arcade, etc.</para>
///
/// <para>Values are read defensively: WT data members are mostly getter
/// properties with no backing field, so the reads try the field first and fall
/// back to the <c>get_</c> accessor (the IL2CPP getter rule bites concrete
/// types, but these gameplay singletons dispatch their getters fine — same as
/// <see cref="CurrencyReader"/>). Nothing here computes a value; every number
/// comes from the game's own state.</para>
/// </summary>
public static class WorldTourStateService
{
    private const string CITY_MANAGER = "app.worldtour.WTCityManager";
    private const string SECTION_MANAGER = "app.worldtour.CitySectionManager";
    private const string GUI_MANAGER = "app.GuiManager";
    private const string KPI_WT = "app.Kpi.WT";
    private const string AVATAR_MANAGER = "app.worldtour.avatar.AvatarManager";
    // Global (not WT-specific) camera singleton; consumed here by the WT field
    // radar for camera-relative clock directions.
    private const string CAMERA_MANAGER = "app.CameraManager";

    // WTCityManager.EState: the "we are in a World Tour city" state.
    private const int E_STATE_ACTIVATED = 1;   // EState { Deactivated = 0, Activated = 1 }

    /// <summary>
    /// The reliable "the player is in the World Tour field" signal. Prefers
    /// <c>WTCityManager.IsActivated()</c>; falls back to the <c>mState</c> field
    /// (EState.Activated) if the method can't be dispatched. False whenever the
    /// city manager is absent (menus, boot, other modes).
    /// </summary>
    public static bool IsInWorldTour()
    {
        var mgr = GetCityManager();
        if (mgr == null) return false;

        var activated = FlowHelper.Call(mgr, "IsActivated");
        if (activated is bool b) return b;

        // Fallback: read the state field directly.
        int state = FlowHelper.ReadIntField(mgr, "mState", -1);
        return state == E_STATE_ACTIVATED;
    }

    /// <summary>Current city id (<c>WTCityManager.mCityId</c>), or 0 when unavailable.</summary>
    public static uint CityId => ReadUIntFromCityManager("mCityId");

    /// <summary>Current situation id (<c>WTCityManager.mSituationId</c>), or 0.</summary>
    public static uint SituationId => ReadUIntFromCityManager("mSituationId");

    /// <summary>A copied city/situation pair with explicit readability, for retained spoken targets.</summary>
    internal static bool TryReadContext(out uint city, out uint situation)
    {
        city = situation = 0;
        var manager = GetCityManager();
        // Both native fields are uint. General state accessors use zero for failed reads;
        // retained-target validation must distinguish that failure from a readable value.
        if (MemberAccess.ReadFieldsFirst(manager, "mCityId", typeof(uint)) is not uint currentCity
            || MemberAccess.ReadFieldsFirst(manager, "mSituationId", typeof(uint)) is not uint currentSituation)
            return false;
        city = currentCity;
        situation = currentSituation;
        return true;
    }

    /// <summary>Time-of-day (<c>WTCityManager.mTimeType</c> → WTDefine.TimeType:
    /// Invalid=0, Day=2, Night=4, Midnight=5), or 0.</summary>
    public static uint TimeType => ReadUIntFromCityManager("mTimeType");

    /// <summary>Weather (<c>WTCityManager.mWeatherType</c> → WTDefine.WeatherType:
    /// Fine, Cloudy, Foggy, Rainy, Snowy, Storm), or 0.</summary>
    public static uint WeatherType => ReadUIntFromCityManager("mWeatherType");

    /// <summary>The sub-area the player stands in
    /// (<c>CitySectionManager.CurrentSectionId</c>), or 0 when unavailable.
    /// Poll this for area-change detection.</summary>
    public static uint CurrentSectionId
    {
        get
        {
            // Read at most every SECTION_REFRESH_MS: some callers ask every frame, and each
            // read creates engine wrappers (the GC-thread crash family).
            long now = System.Environment.TickCount64;
            if (now - _sectionReadAt < SECTION_REFRESH_MS) return _sectionId;
            _sectionReadAt = now;
            return _sectionId = ReadSectionId();
        }
    }

    // A quarter second: the zone reader's own poll cadence (ZoneHooks.POLL_INTERVAL at 60 fps).
    private const long SECTION_REFRESH_MS = 250;
    private static long _sectionReadAt;
    private static uint _sectionId;

    private static uint ReadSectionId()
    {
        uint id = 0;
        string route = "manager";
        var mgr = GetSectionManager();
        if (mgr != null)
        {
            id = (uint)FlowHelper.ReadIntField(mgr, "CurrentSectionId", 0);
            if (id == 0 && FlowHelper.Call(mgr, "get_CurrentSectionId") is object boxed)
                id = System.Convert.ToUInt32(boxed);
        }
        if (id == 0) { id = KpiSectionId(); route = "Kpi.WT"; }
        if (id != 0 && !_sectionRouteLogged)
        {
            _sectionRouteLogged = true;
            API.LogInfo($"[SF6Access] Section id {id} read through {route}");
        }
        return id;
    }

    private static bool _sectionRouteLogged;
    private static REFrameworkNET.Method _kpiSection;
    private static bool _kpiSectionResolved;

    /// <summary><c>app.Kpi.WT.GetCurrentSectionId()</c> — a static the game's telemetry uses.</summary>
    private static uint KpiSectionId()
    {
        if (!_kpiSectionResolved)
        {
            _kpiSectionResolved = true;
            _kpiSection = TDB.Get().FindType(KPI_WT)?.GetMethod("GetCurrentSectionId()");
            API.LogInfo($"[SF6Access] {KPI_WT}.GetCurrentSectionId {(_kpiSection == null ? "not found" : "bound")}");
        }
        if (_kpiSection == null) return 0;
        try { return System.Convert.ToUInt32(_kpiSection.InvokeBoxed(typeof(uint), null, null)); }
        catch { return 0; }
    }

    /// <summary><c>app.GuiManager.AccessData</c> — the game's registry of map-visible NPCs,
    /// facilities and the section manager. Not a singleton itself.</summary>
    public static ManagedObject GetAccessData() =>
        FlowHelper.GetObjectField(Singleton(GUI_MANAGER, ref _guiManager), "AccessData");

    /// <summary>The AvatarManager singleton (owner of the interactable-access
    /// list) for the field radar reader. Null outside the field.</summary>
    public static ManagedObject GetAvatarManager() => Singleton(AVATAR_MANAGER, ref _avatarManager);

    /// <summary>The WTCityManager singleton, cached.</summary>
    public static ManagedObject GetCityManager() => Singleton(CITY_MANAGER, ref _cityManager);

    /// <summary>The CitySectionManager. It is owned by <see cref="GetAccessData"/>, not a
    /// singleton; the singleton lookup is kept only as a fallback.</summary>
    public static ManagedObject GetSectionManager() =>
        FlowHelper.GetObjectField(GetAccessData(), "SectionManager") ?? Singleton(SECTION_MANAGER, ref _sectionManager);

    /// <summary>The global <c>app.CameraManager</c> singleton (camera position /
    /// look-at / rotation), for camera-relative direction announcements.</summary>
    public static ManagedObject GetCameraManager() => Singleton(CAMERA_MANAGER, ref _cameraManager);

    private static ManagedObject _cityManager;
    private static ManagedObject _sectionManager;
    private static ManagedObject _guiManager;
    private static ManagedObject _avatarManager;
    private static ManagedObject _cameraManager;

    private static uint ReadUIntFromCityManager(string field)
    {
        var mgr = GetCityManager();
        if (mgr == null) return 0;
        return (uint)FlowHelper.ReadIntField(mgr, field, 0);
    }

    /// <summary>Fetch a managed singleton by FullName, re-validated EVERY call
    /// (stale-param rule): the game recreates these managers on scene load — a
    /// pointer cached during the WT loading screen goes dead once the field
    /// spawns, and every read on it silently returns null/0. So the fresh lookup
    /// is authoritative; the cache only preserves wrapper identity while the
    /// underlying address is unchanged.</summary>
    private static ManagedObject Singleton(string fullName, ref ManagedObject cache)
    {
        ManagedObject fresh;
        try { fresh = API.GetManagedSingleton(fullName) as ManagedObject; }
        catch { fresh = null; }

        if (fresh == null) { cache = null; return null; }
        try
        {
            if (cache == null || cache.GetAddress() != fresh.GetAddress())
                cache = fresh;   // instance replaced — re-bind, never trust the old pointer
        }
        catch { cache = fresh; }
        return cache;
    }
}
