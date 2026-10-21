using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>Refreshes an exact selected actor using current live lists, never a stored native pointer.</summary>
public static class LiveDestinationReader
{
    private const int MaximumRegistryEntries = 512; // Same defensive bound as AccessTargetCatalog.

    public static DestinationActor ForAvatar(AvatarFieldReader.Other other)
    {
        var named = AvatarNameCache.Classify(other);
        return Identity(DestinationActorSource.Avatar, other.Avatar, named.Name, named.Kind);
    }

    public static DestinationActor ForAccess(ManagedObject target, string name, int kind)
        => Identity(DestinationActorSource.Registry, target, name, kind);

    private static DestinationActor Identity(DestinationActorSource source, ManagedObject actor, string name, int kind)
    {
        var owner = FlowHelper.Call(actor, "get_GameObject") as ManagedObject;
        return new DestinationActor(source, actor?.GetAddress() ?? 0, owner?.GetAddress() ?? 0,
            WorldTourStateService.CityId, name, kind);
    }

    public static ActorTrackingState.Observation Read(DestinationActor expected)
    {
        uint city = WorldTourStateService.CityId;
        if (city == 0) return default;
        if (city != expected.City) return new(true, default);
        bool readable;
        ManagedObject actor;
        if (expected.Source == DestinationActorSource.Avatar)
            readable = AvatarFieldReader.TryFindOther(expected.Address, out actor);
        else
        {
            var data = WorldTourStateService.GetAccessData();
            var list = FlowHelper.Call(data, "GetAccessTargetData", expected.Kind) as ManagedObject;
            readable = AvatarFieldReader.TryFindInList(list, expected.Address, MaximumRegistryEntries, out actor);
        }
        if (!readable) return default;
        if (actor == null) return new(true, default);

        var named = expected.Source == DestinationActorSource.Avatar ? AvatarFieldReader.Classify(actor)
            : (FlowHelper.CleanTags(FlowHelper.Call(actor, "GetDispName") as string)?.Trim(), AvatarFieldReader.ReadContactType(actor));
        if (string.IsNullOrEmpty(named.Item1)
            || expected.Source == DestinationActorSource.Registry && named.Item2 < 0) return default;
        var current = Identity(expected.Source, actor, named.Item1, named.Item2);
        if (!current.IsValid) return default;
        if (ActorTrackingState.ClassificationIncomplete(expected, current)) return default;
        if (current != expected) return new(true, current);
        var position = AvatarFieldReader.ReadWorldPos(actor);
        if (!position.ok) return default;
        bool contact = false;
        try { contact = AtContact(current); } catch { }
        return new(true, current, position.x, position.y, position.z, contact);
    }

    private static bool AtContact(DestinationActor actor)
    {
        var list = AvatarFieldReader.GetAccessInfoList(WorldTourStateService.GetAvatarManager());
        int count = System.Math.Min(FlowHelper.GetListCount(list), MaximumRegistryEntries);
        for (int i = 0; i < count; i++)
        {
            var info = MemberAccess.ReadFieldsFirst(FlowHelper.GetListItem(list, i), "TargetInfo");
            var target = MemberAccess.ReadFieldsFirst(info, "Target") as ManagedObject;
            if (target == null) continue;
            if (actor.Source == DestinationActorSource.Registry && target.GetAddress() == actor.Address) return true;
            var owner = FlowHelper.Call(target, "get_GameObject") as ManagedObject;
            if (owner?.GetAddress() == actor.Owner) return true;
        }
        return false;
    }
}
