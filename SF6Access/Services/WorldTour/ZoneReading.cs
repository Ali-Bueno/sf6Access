namespace SF6Access.Services.WorldTour;

/// <summary>Where a zone name came from, because the three routes are NOT equally
/// precise and the player must not be told otherwise.</summary>
public enum ZoneSource
{
    /// <summary>Nothing resolved.</summary>
    None,
    /// <summary>The game's own district/section the player is standing in — real
    /// containment.</summary>
    Section,
    /// <summary>The nearest named fast-travel landmark. A direction-less
    /// proximity answer, not containment, so it is spoken as "near X".</summary>
    NearestPoint,
    /// <summary>The city. Last resort — true, but coarse.</summary>
    City,
}

/// <summary>One answer to "where am I?".</summary>
public readonly struct ZoneReading
{
    public readonly string Name;
    public readonly ZoneSource Source;
    /// <summary>Ground-plane metres to the named landmark. Only meaningful for
    /// <see cref="ZoneSource.NearestPoint"/>; the other routes are containment.</summary>
    public readonly float Distance;

    public ZoneReading(string name, ZoneSource source, float distance)
    {
        Name = name; Source = source; Distance = distance;
    }

    public bool Ok => Source != ZoneSource.None && !string.IsNullOrEmpty(Name);
}
