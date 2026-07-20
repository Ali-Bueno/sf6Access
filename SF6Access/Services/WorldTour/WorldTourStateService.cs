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
            var mgr = GetSectionManager();
            if (mgr == null) return 0;
            uint id = (uint)FlowHelper.ReadIntField(mgr, "CurrentSectionId", 0);
            if (id != 0) return id;
            var boxed = FlowHelper.Call(mgr, "get_CurrentSectionId");
            return boxed != null ? System.Convert.ToUInt32(boxed) : 0;
        }
    }

    /// <summary>The AvatarManager singleton (owner of the interactable-access
    /// list) for the field radar reader. Null outside the field.</summary>
    public static ManagedObject GetAvatarManager() => Singleton(AVATAR_MANAGER, ref _avatarManager);

    /// <summary>The WTCityManager singleton, cached.</summary>
    public static ManagedObject GetCityManager() => Singleton(CITY_MANAGER, ref _cityManager);

    /// <summary>The CitySectionManager singleton, cached.</summary>
    public static ManagedObject GetSectionManager() => Singleton(SECTION_MANAGER, ref _sectionManager);

    /// <summary>The global <c>app.CameraManager</c> singleton (camera position /
    /// look-at / rotation), for camera-relative direction announcements.</summary>
    public static ManagedObject GetCameraManager() => Singleton(CAMERA_MANAGER, ref _cameraManager);

    private static ManagedObject _cityManager;
    private static ManagedObject _sectionManager;
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
