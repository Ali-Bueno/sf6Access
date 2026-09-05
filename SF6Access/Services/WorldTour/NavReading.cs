namespace SF6Access.Services.WorldTour;

// The World Tour navigation radar's STATE MODEL: what one sweep of the avatar's
// own sensing rays means, with no idea of how it was measured or how it will be
// announced. FieldNavRadarService fills it in; FieldNavRadarHooks speaks it.

/// <summary>What is in front of the avatar, expressed as an obstacle CLASS rather
/// than a number. The class is derived from WHICH of the game's own named forward
/// rays report a contact — the height stack is the measurement, so no offset or
/// threshold is invented here.</summary>
public enum FrontProfile
{
    /// <summary>No forward ray of the height stack hit: walkable.</summary>
    Open,
    /// <summary>Only the low rays hit — a kerb or a low prop, steppable.</summary>
    Step,
    /// <summary>Up to the waist ray — a railing, a counter, a low wall.</summary>
    WaistHigh,
    /// <summary>Up to the bust ray — a wall.</summary>
    Wall,
    /// <summary>The high-wall ray too — a wall with nothing above it to climb.</summary>
    TallWall,
}

/// <summary>One sample of the navigation radar. <see cref="Ok"/> false means the
/// sample could not be taken at all (not in the field, API unreachable) and must
/// never be compared against a previous reading.</summary>
public readonly struct NavReading
{
    public readonly bool Ok;
    public readonly FrontProfile Front;
    /// <summary>True when at least one forward ray produced a usable contact
    /// distance (the near stack or the long forward reach).</summary>
    public readonly bool HasDistance;
    /// <summary>Metres to the nearest forward contact. RE Engine world units are
    /// metres, and the distance is the engine's own <c>ContactPoint.Distance</c>.</summary>
    public readonly float Distance;
    public readonly bool LeftBlocked;
    public readonly bool RightBlocked;
    /// <summary>False when the downward ray found nothing — a ledge or a hole.</summary>
    public readonly bool GroundSolid;

    public NavReading(FrontProfile front, bool hasDistance, float distance,
                      bool leftBlocked, bool rightBlocked, bool groundSolid)
    {
        Ok = true;
        Front = front;
        HasDistance = hasDistance;
        Distance = distance;
        LeftBlocked = leftBlocked;
        RightBlocked = rightBlocked;
        GroundSolid = groundSolid;
    }

    /// <summary>Whether two readings describe the SAME situation. Distance is
    /// deliberately excluded: it changes with every step, and folding it in would
    /// make every sample a "state change" and defeat the whole reactive design.</summary>
    public bool SameStateAs(NavReading other) =>
        Ok == other.Ok && Front == other.Front && LeftBlocked == other.LeftBlocked
        && RightBlocked == other.RightBlocked && GroundSolid == other.GroundSolid;
}
