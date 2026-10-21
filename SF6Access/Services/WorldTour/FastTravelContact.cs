using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>Identifies a live fast-travel object, without inferring interaction or unlock state from distance.</summary>
internal static class FastTravelContact
{
    private const string PointObjectType = "app.worldtour.om.Om022000";
    private static bool _logged;

    /// <summary>The caller must obtain the target from CurrentAccessInfoList before saying "Interact".</summary>
    internal static bool TryDescribe(ManagedObject target, string nativeName, out string description)
    {
        description = null;
        if (target == null) return false;
        var om = MemberAccess.ReadFieldsFirst(target, "OmComp") as ManagedObject;
        bool pointObject = om?.GetTypeDefinition()?.GetFullName() == PointObjectType;
        bool semantic = MemberAccess.FindMethod(target.GetTypeDefinition(), "IsFastTravelOpenOM", logMiss: false) != null
            && FlowHelper.Call(target, "IsFastTravelOpenOM") is true;
        if (!pointObject && !semantic) return false;

        // Om022000 owns the controller, whose exact PointID links to the localized point table.
        // Do not use a nearby landmark's name for an unrelated target or claim an unlock state.
        uint id = 0;
        if (pointObject)
        {
            var controller = MemberAccess.ReadFieldsFirst(om, "FastTravelCtrl") as ManagedObject;
            if (MemberAccess.ReadFieldsFirst(controller, "PointID", typeof(uint)) is uint value) id = value;
        }
        string name = nativeName;
        if (string.IsNullOrWhiteSpace(name) && id > 0)
            foreach (var point in FastTravelCatalog.Points())
                if (point.Id > 0 && (uint)point.Id == id) { name = point.Name; break; }
        description = string.IsNullOrWhiteSpace(name) ? DestinationText.FastTravelKind()
            : DeviceMapText.Icon(name, DestinationText.FastTravelKind());
        if (!_logged)
        {
            _logged = true;
            API.LogInfo($"[SF6Access] Fast-travel contact identified: point {id}, object={pointObject}, semantic={semantic}, '{description}'");
        }
        return true;
    }
}
