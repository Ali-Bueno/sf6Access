namespace SF6Access.Services.WorldTour;

/// <summary>Where a zone name came from, which decides how confidently it is worded.</summary>
public enum ZoneSource
{
    None,
    /// <summary>The game's own city section — the player is inside it.</summary>
    Section,
    /// <summary>The area around the nearest fast-travel landmark (our own division).</summary>
    Region,
    /// <summary>Only the city is known.</summary>
    City,
}

/// <summary>One answer to "which part of town is this".</summary>
public readonly struct ZoneReading
{
    public readonly string Name;
    public readonly ZoneSource Source;
    public readonly float Distance;
    /// <summary>The game's section id for a <see cref="ZoneSource.Section"/> reading, else 0.</summary>
    public readonly uint SectionId;

    public ZoneReading(string name, ZoneSource source, float distance, uint sectionId = 0)
    {
        Name = name; Source = source; Distance = distance; SectionId = sectionId;
    }

    public bool Ok => Source != ZoneSource.None && !string.IsNullOrEmpty(Name);
}
