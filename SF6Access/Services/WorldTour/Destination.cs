#nullable enable
namespace SF6Access.Services.WorldTour;

/// <summary>The destination picker's categories, in the order it cycles them.</summary>
public enum DestinationCategory { Mission, KnownPlaces, Rest, Places, Travel, Access, Shops, Masters, Missions, Jobs, People, Pins, Saved }

/// <summary>What choosing an entry does: guide to it, hand guidance back to the story, or save the spot.</summary>
public enum DestinationAction { Go, FollowMission, SaveHere, Explore, ListenCity }

public enum DestinationActorSource { None, Avatar, Registry }

/// <summary>A live actor's exact instance identity, copied without retaining native wrappers.</summary>
public readonly record struct DestinationActor(DestinationActorSource Source, ulong Address,
    ulong Owner, uint City, string Name, int Kind)
{
    public bool IsValid => Source != DestinationActorSource.None && Address != 0 && Owner != 0
        && City != 0 && !string.IsNullOrWhiteSpace(Name);
}

/// <summary>A named world point the player can choose to be guided to.</summary>
public readonly record struct Destination(
    string Name, string? Kind, DestinationCategory Category, float X, float Y, float Z,
    DestinationAction Action = DestinationAction.Go, DestinationActor Actor = default)
{
    /// <summary>"Beat Square, fast travel" — name plus the kind word when there is one.</summary>
    public string Label => string.IsNullOrEmpty(Kind) ? Name : Name + ", " + Kind;

    /// <summary>Whether the entry carries a world position; an exact origin means "none".</summary>
    public bool HasPosition => Action is not (DestinationAction.SaveHere or DestinationAction.Explore or DestinationAction.ListenCity)
        && float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z)
        && (X != 0f || Y != 0f || Z != 0f);

    public float FlatDistanceTo(float x, float z)
    {
        float dx = X - x, dz = Z - z;
        return (float)System.Math.Sqrt(dx * dx + dz * dz);
    }
}
