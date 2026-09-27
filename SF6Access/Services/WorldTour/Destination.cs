#nullable enable
namespace SF6Access.Services.WorldTour;

/// <summary>The destination picker's categories, in the order it cycles them.</summary>
public enum DestinationCategory { Mission, Places, Access, Shops, Masters, Missions, Jobs, People, Pins, Saved }

/// <summary>What choosing an entry does: guide to it, hand guidance back to the story, or save the spot.</summary>
public enum DestinationAction { Go, FollowMission, SaveHere }

/// <summary>A named world point the player can choose to be guided to.</summary>
public readonly record struct Destination(
    string Name, string? Kind, DestinationCategory Category, float X, float Y, float Z,
    DestinationAction Action = DestinationAction.Go)
{
    /// <summary>"Beat Square, fast travel" — name plus the kind word when there is one.</summary>
    public string Label => string.IsNullOrEmpty(Kind) ? Name : Name + ", " + Kind;

    /// <summary>Whether the entry carries a world position; an exact origin means "none".</summary>
    public bool HasPosition => Action != DestinationAction.SaveHere && (X != 0f || Y != 0f || Z != 0f);

    public float FlatDistanceTo(float x, float z)
    {
        float dx = X - x, dz = Z - z;
        return (float)System.Math.Sqrt(dx * dx + dz * dz);
    }
}
