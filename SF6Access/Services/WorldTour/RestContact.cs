using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>Names a native home contact without inferring availability from nearby hangout landmarks.</summary>
internal static class RestContact
{
    private static bool _logged;

    /// <summary>The caller must obtain this target from the game's CurrentAccessInfoList.</summary>
    internal static bool TryDescribe(ManagedObject target, string nativeName, out string description)
    {
        description = null;
        if (target == null || MemberAccess.FindMethod(target.GetTypeDefinition(), "IsHome", logMiss: false) == null
            || FlowHelper.Call(target, "IsHome") is not true) return false;

        description = string.IsNullOrWhiteSpace(nativeName) ? DestinationText.RestKind()
            : DeviceMapText.Icon(nativeName, DestinationText.RestKind());
        if (!_logged)
        {
            _logged = true;
            API.LogInfo($"[SF6Access] Native hangout contact identified by IsHome: '{description}'");
        }
        return true;
    }
}
