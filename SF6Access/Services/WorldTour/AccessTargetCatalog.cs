using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Live named people and facilities from the game's own map registry,
/// <c>app.GuiManager.AccessData</c> (<c>GUIAccessDataManager</c>) — the data the phone
/// map builds its NPC, master and facility icons from — so the picker needs no open map.
/// </summary>
public static class AccessTargetCatalog
{
    private const string OM_TYPE_ENUM = "app.GUIAccessDataManager.OMType";

    /// <summary>Safety bound on entries read per list, only so a corrupt count cannot stall a
    /// press; the picker sorts what is read, so this must stay well above a city's real count.</summary>
    private const int MAX_READ = 512;

    private const long EMPTY_RETRY_MS = 5000;

    // Facilities do not move and their names cost a message lookup each: read once per city.
    private static List<Destination> _facilities;
    private static uint _facilitiesCity;
    private static long _facilitiesAt;

    private static bool _logged;

    public static void AddTo(List<Destination> into)
    {
        var data = WorldTourStateService.GetAccessData();
        if (data == null)
        {
            LogOnce("app.GuiManager.AccessData not readable");
            return;
        }

        int masters = AddTargets(data, AvatarFieldReader.CONTACT_LEGENDARY, into);
        int npcs = AddTargets(data, AvatarFieldReader.CONTACT_NPC, into);
        var facilities = Facilities(data);
        into.AddRange(facilities);
        LogOnce($"{masters} masters, {npcs} NPCs, {facilities.Count} facilities");
    }

    private static int AddTargets(ManagedObject data, int contactType, List<Destination> into)
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
            into.Add(new Destination(name, null, category, x, y, z));
            added++;
        }
        return added;
    }

    private static List<Destination> Facilities(ManagedObject data)
    {
        uint city = WorldTourStateService.CityId;
        long now = System.Environment.TickCount64;
        if (_facilities != null && city == _facilitiesCity &&
            (_facilities.Count > 0 || now - _facilitiesAt < EMPTY_RETRY_MS))
            return _facilities;

        _facilitiesCity = city;
        _facilitiesAt = now;
        _facilities = new List<Destination>();
        int shop = FieldProbeService.EnumValue(OM_TYPE_ENUM, "Shop");
        if (shop < 0) return _facilities;

        var list = FlowHelper.Call(data, "GetWTCityOMSetterData_OM", shop) as ManagedObject;
        int n = System.Math.Min(FlowHelper.GetListCount(list), MAX_READ);
        for (int i = 0; i < n; i++)
        {
            var setter = FlowHelper.GetListItem(list, i);
            // GetDispName returns the message Guid, a struct: resolve it with its bytes pinned.
            string name = FlowHelper.Call(setter, "GetDispName") is REFrameworkNET.ValueType guid
                ? PinnedValue.Use(guid, _ => FlowHelper.ResolveGuid(guid))?.Trim() : null;
            var (x, y, z, ok) = AvatarFieldReader.ReadWorldPos(setter);
            if (string.IsNullOrEmpty(name) || !ok || x == 0f && y == 0f && z == 0f) continue;

            string kind = DeviceMapText.IconType(FlowHelper.CallInt(setter, "GetFacilityIconType"));
            _facilities.Add(new Destination(name, kind, DestinationCategory.Shops, x, y, z));
        }
        return _facilities;
    }

    private static void LogOnce(string what)
    {
        if (_logged) return;
        _logged = true;
        API.LogInfo($"[SF6Access] Destinations from the game's map registry: {what}");
    }
}
