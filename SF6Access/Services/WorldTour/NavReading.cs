namespace SF6Access.Services.WorldTour;

// The World Tour navigation radar's STATE MODEL: what one sweep of the avatar's
// own sensing rays means, with no idea of how it was measured or how it will be
// announced. FieldNavRadarService fills it in; FieldNavRadarHooks speaks it.

/// <summary>What is in front of the avatar, expressed as an obstacle CLASS rather
/// than a number. The class is derived from WHICH of the game's own named forward
/// rays report a contact — the height stack is the measurement, so no offset or
/// threshold is invented here.
///
/// <para><b>This is a DESCRIPTION of what lies ahead, not a verdict.</b> The rays of
/// the stack have different reaches (the near rungs about 1.3 m, the long one 2.0 m),
/// so a rung that hits means "there is something of about that height within its own
/// reach", never "you are standing against it". Whether the avatar is actually
/// stopped is <see cref="NavReading.Block"/>, which comes from the game's own
/// collision verdict — see <see cref="FieldNavVerdictService"/>.</para></summary>
public enum FrontProfile
{
    /// <summary>No forward ray of the height stack hit: walkable.</summary>
    Open,
    /// <summary>Only the low rays hit — a kerb or a low prop — or the game's own
    /// step-up check says it will climb whatever is there by itself.</summary>
    Step,
    /// <summary>Up to the waist ray — a railing, a counter, a low wall.</summary>
    WaistHigh,
    /// <summary>Up to the bust ray — a wall.</summary>
    Wall,
    /// <summary>The high-wall ray too — a wall with nothing above it to climb.</summary>
    TallWall,
}

/// <summary>Whether the avatar is being STOPPED right now, taken from the game's own
/// collision verdict rather than inferred from ray heights.
///
/// <para>This split is the fix for the two false positives reported in play: "open"
/// announced while a waist-high wall was in fact blocking the avatar (a ray of the
/// stack simply did not reach it), and "wall" announced for things the avatar walks
/// straight over (a kerb the game auto-steps). The rays cannot answer "am I stopped"
/// — only the collision the game already resolves every frame can.</para></summary>
public enum FrontBlock
{
    /// <summary>Nothing is stopping the avatar. Something may still lie ahead:
    /// <see cref="NavReading.Front"/> describes it and <see cref="NavReading.Distance"/>
    /// says how far.</summary>
    None,
    /// <summary>The avatar is against a wall the game says it can RUN ALONG. A
    /// surface, not a dead end, so it is never cued as impassable.</summary>
    WallRide,
    /// <summary>The game reports a wall contact that stops forward movement.</summary>
    Blocked,
}

/// <summary>One sample of the navigation radar. <see cref="Ok"/> false means the
/// sample could not be taken at all (not in the field, API unreachable) and must
/// never be compared against a previous reading.</summary>
public readonly struct NavReading
{
    public readonly bool Ok;
    /// <summary>The game's own "am I stopped" verdict. This — never
    /// <see cref="Front"/> — is what the announcer treats as blocked.</summary>
    public readonly FrontBlock Block;
    /// <summary>What lies ahead, as a height class. A description; see
    /// <see cref="FrontProfile"/>.</summary>
    public readonly FrontProfile Front;
    /// <summary>True when at least one forward ray produced a usable contact
    /// distance (the near stack or the long forward reach).</summary>
    public readonly bool HasDistance;
    /// <summary>Metres to the nearest forward contact. RE Engine world units are
    /// metres, and the distance is the engine's own <c>ContactPoint.Distance</c>.</summary>
    public readonly float Distance;
    /// <summary>True when the longest forward probe (<c>FRONT_LONG</c>) found nothing
    /// over its whole reach. It is what makes "the way opened" mean something: leaving
    /// a wall with another wall a metre further on is not an exit, and cueing it as
    /// one is the open/closed chatter this flag exists to suppress.</summary>
    public readonly bool LongRangeClear;
    public readonly bool LeftBlocked;
    public readonly bool RightBlocked;
    /// <summary>False when the downward ray found nothing — a ledge or a hole.</summary>
    public readonly bool GroundSolid;

    public NavReading(FrontBlock block, FrontProfile front, bool hasDistance, float distance,
                      bool longRangeClear, bool leftBlocked, bool rightBlocked, bool groundSolid)
    {
        Ok = true;
        Block = block;
        Front = front;
        HasDistance = hasDistance;
        Distance = distance;
        LongRangeClear = longRangeClear;
        LeftBlocked = leftBlocked;
        RightBlocked = rightBlocked;
        GroundSolid = groundSolid;
    }

    /// <summary>Whether two readings describe the SAME situation. Distance is
    /// deliberately excluded: it changes with every step, and folding it in would
    /// make every sample a "state change" and defeat the whole reactive design.
    /// <see cref="LongRangeClear"/> is excluded for the same reason — it is a
    /// qualifier the announcer consults when a block opens, not a situation of its
    /// own, and letting it drive confirmations would restart the debounce every time
    /// a distant wall entered the long ray's reach.</summary>
    public bool SameStateAs(NavReading other) =>
        Ok == other.Ok && Block == other.Block && Front == other.Front
        && GroundSolid == other.GroundSolid;
}
