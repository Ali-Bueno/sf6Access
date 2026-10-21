using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Live named people and known static facilities from the game's own map registry,
/// <c>app.GuiManager.AccessData</c> (<c>GUIAccessDataManager</c>) — the data the phone
/// map builds its icons from; remembered facility coordinates do not imply current availability.
/// </summary>
public static class AccessTargetCatalog
{
    private const string OM_TYPE_ENUM = "app.GUIAccessDataManager.OMType";

    /// <summary>Safety bound on entries read per list, only so a corrupt count cannot stall a
    /// press; the picker sorts what is read, so this must stay well above a city's real count.</summary>
    private const int MAX_READ = 512;

    private static readonly FacilityMemory _facilities = new();

    private static bool _logged, _unreadableLogged;

    /// <param name="npcs">False skips the plain NPC list (masters and facilities only).</param>
    public static void AddTo(List<Destination> into, bool npcs = true)
    {
        uint city = WorldTourStateService.CityId;
        var cityManager = WorldTourStateService.GetCityManager();
        if (!_facilities.Bind(city, cityManager?.GetAddress() ?? 0)) return;
        var data = WorldTourStateService.GetAccessData();
        if (data == null)
        {
            if (!_unreadableLogged) API.LogInfo("[SF6Access] Destinations: app.GuiManager.AccessData not readable");
            _unreadableLogged = true;
            into.AddRange(_facilities.Places);
            return;
        }

        int masters = AddTargets(data, AvatarFieldReader.CONTACT_LEGENDARY, into, shopsOnly: false);
        // Without the crowd, NPC shopkeepers still count: they are places to go.
        int people = AddTargets(data, AvatarFieldReader.CONTACT_NPC, into, shopsOnly: !npcs);
        var facilities = Facilities(data, city);
        into.AddRange(facilities);
        if (npcs) LogOnce($"{masters} masters, {people} NPCs, {facilities.Count} facilities");
    }

    private static int AddTargets(ManagedObject data, int contactType, List<Destination> into, bool shopsOnly)
    {
        var list = FlowHelper.Call(data, "GetAccessTargetData", contactType) as ManagedObject;
        int n = System.Math.Min(FlowHelper.GetListCount(list), MAX_READ), added = 0;
        for (int i = 0; i < n; i++)
        {
            var target = FlowHelper.GetListItem(list, i);
            string name = FlowHelper.CleanTags(FlowHelper.Call(target, "GetDispName") as string)?.Trim();
            var (x, y, z, ok) = AvatarFieldReader.ReadWorldPos(target);
            if (string.IsNullOrEmpty(name) || !ok || x == 0f && y == 0f && z == 0f) continue;

            var category = contactType == AvatarFieldReader.CONTACT_LEGENDARY ? DestinationCategory.Masters
                : FlowHelper.GetObjectField(target, "ShopComponent") != null ? DestinationCategory.Shops
                : DestinationCategory.People;
            if (shopsOnly && category == DestinationCategory.People) continue;
            into.Add(new Destination(name, null, category, x, y, z,
                Actor: LiveDestinationReader.ForAccess(target, name, contactType)));
            added++;
        }
        return added;
    }

    private static IReadOnlyList<Destination> Facilities(ManagedObject data, uint city)
    {
        long now = System.Environment.TickCount64;
        if (!_facilities.TryBeginRefresh(now)) return _facilities.Places;
        int shop = FieldProbeService.EnumValue(OM_TYPE_ENUM, "Shop");
        if (shop < 0) return _facilities.Places;

        var list = FlowHelper.Call(data, "GetWTCityOMSetterData_OM", shop) as ManagedObject;
        int n = System.Math.Min(FlowHelper.GetListCount(list), MAX_READ);
        int before = _facilities.Places.Count;
        for (int i = 0; i < n; i++)
        {
            var setter = FlowHelper.GetListItem(list, i);
            // GetDispName returns the message Guid, a struct: resolve it with its bytes pinned.
            string name = FlowHelper.Call(setter, "GetDispName") is REFrameworkNET.ValueType guid
                ? PinnedValue.Use(guid, _ => FlowHelper.ResolveGuid(guid))?.Trim() : null;
            var (x, y, z, ok) = AvatarFieldReader.ReadWorldPos(setter);
            if (string.IsNullOrEmpty(name) || !ok || x == 0f && y == 0f && z == 0f) continue;

            string kind = DeviceMapText.IconType(FlowHelper.CallInt(setter, "GetFacilityIconType"));
            _facilities.Remember(new Destination(name, kind, DestinationCategory.Shops, x, y, z), DestinationCatalog.MergeM);
        }
        if (_facilities.Places.Count != before)
            API.LogInfo($"[SF6Access] Known facilities: city {city}, {_facilities.Places.Count} places");
        return _facilities.Places;
    }

    private static void LogOnce(string what)
    {
        if (_logged) return;
        _logged = true;
        API.LogInfo($"[SF6Access] Destinations from the game's map registry: {what}");
    }
}
