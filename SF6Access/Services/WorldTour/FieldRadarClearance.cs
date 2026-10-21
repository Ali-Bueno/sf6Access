namespace SF6Access.Services.WorldTour;

/// <summary>What a direction IS. Not "unknown yet" as an error — a direction whose
/// clearance sits in the dead band genuinely has no answer worth speaking.</summary>
internal enum PassState { Unknown, Passable, Blocked }

/// <summary>
/// The radar's DECIDING layer, and the answer to the question the player actually
/// asks: <b>can I go that way?</b>
///
/// <para><b>Why this replaced the line memory.</b> A Hero's Call's radar remembers the
/// LINE of the wall each beam looks at and speaks when that line breaks. It is a
/// faithful model of a tile dungeon, where a slope change in the geometry is a corner
/// or a door and nothing else. A measured session in Metro City logged 109 cues in
/// 90 seconds at 109 DIFFERENT places, with no repetition: the detector was not
/// malfunctioning, it was correctly reporting that a city facade changes slope about
/// once a second — shopfront recesses, columns, awnings, kerbs, benches. The event
/// model has no defence against that, because the event it fires on is real.</para>
///
/// <para><b>And the line break answers the wrong question.</b> Its "open" means "this
/// beam now reaches further past where the boundary was" — a depth discontinuity, not
/// a passage. A doorway recess 40 cm deep produces one. So does the space behind a
/// bench. The player was told "exit to your right", turned right, and the spoken
/// readout — which asks the game's own collision whether the avatar is blocked —
/// said blocked. The two channels disagreed because they were answering two different
/// questions. This layer asks the readout's question.</para>
///
/// <para><b>The measurement.</b> Clearance is how far the avatar's BODY can advance
/// along a beam: three parallel rays at the capsule centre and at ±its radius, taking
/// the most obstructed (<see cref="FieldRayCaster.TryCastBodyStack"/>). A beam that
/// grazes a corner cannot report the street behind it, because the outer ray hits the
/// corner. That is what makes a cue and a walk agree.</para>
///
/// <para><b>The discriminator is enormous.</b> Clearance is measured ALONG the beam,
/// so a side alley reports its own depth — tens of metres — while the open side of an
/// ordinary street reports half the street's width, one to three. The two are not
/// near each other, which is why a wide dead band costs nothing and buys total
/// stability: the avatar must physically travel metres to change a verdict, where the
/// line model flipped on a fraction of a degree of camera settle.</para>
///
/// <para><b>State, not event.</b> Nothing is spoken while a verdict holds. Walking a
/// street with a wall to the left is silent for as long as it stays a wall.</para>
/// </summary>
internal static class FieldRadarClearance
{
    private const int N = FieldRadarSense.BEAM_COUNT;

    private static readonly PassState[] State = new PassState[N];
    private static readonly PassState[] Pending = new PassState[N];
    private static readonly long[] PendingSince = new long[N];
    private static readonly bool[] Seeded = new bool[N];

    /// <summary>The settled verdict for a beam; deferred cues must also check the latest measurement.</summary>
    public static PassState Current(RadarBeam beam) => State[(int)beam];

    /// <summary>Feed one beam's body clearance for this evaluation.
    ///
    /// <para>Returns the state the beam has just SETTLED into — the caller's cue to
    /// speak — or <see cref="PassState.Unknown"/> for "nothing to say", which covers
    /// the verdict holding, the dead band, a change still being confirmed, and the
    /// first seed of a beam.</para>
    ///
    /// <para>Silence is NOT decided here. A beam that may not speak yet — a lateral
    /// during a turn, or the beam facing away from travel — still settles normally and
    /// hands its verdict up; the caller holds it until the beam is audible again. That
    /// is deliberate and it fixes a real hole: walking past an alley and turning back
    /// swept the alley into a lateral beam DURING the turn, so the verdict was formed
    /// while silent and the player, now standing at the mouth of it, was told
    /// nothing.</para></summary>
    public static PassState Step(RadarBeam beam, float clearM, float bodyRadiusM, long now)
    {
        int b = (int)beam;
        PassState want = Classify(clearM, bodyRadiusM, State[b]);

        // The seed. Arming the mode, a teleport or a new area records the world as it
        // ALREADY was and says nothing about it, so the player is not met by three cues
        // describing nothing that changed.
        //
        // It happens on the FIRST EVALUATION, not on the first definite verdict. Those
        // are not the same thing and the difference is a real cue: a beam whose
        // clearance starts inside the dead band — an ordinary street is three or four
        // metres to the side, which is neither — holds Unknown, and if the seed waited
        // for a definite verdict it would spend itself on the first alley the player
        // reaches. That is the one cue that matters most.
        if (!Seeded[b])
        {
            Seeded[b] = true;
            State[b] = want;
            Pending[b] = PassState.Unknown;
            return PassState.Unknown;
        }

        if (want == PassState.Unknown || want == State[b])
        {
            Pending[b] = PassState.Unknown;
            return PassState.Unknown;
        }

        // A BLOCKED verdict must persist to be believed — a pedestrian or a passing car
        // crossing a beam for a frame or two is not a wall appearing. A PASSABLE verdict
        // fires on the first sample that sees it, because a transient can only ever make
        // clearance SHORTER: nothing arrives to make the way further open, so an outward
        // jump is either the geometry ending or the blocker leaving, and both are true
        // the instant they happen. See FieldRadarTuning.BlockedConfirmMs.
        if (Pending[b] != want) { Pending[b] = want; PendingSince[b] = now; }
        long confirmMs = want == PassState.Passable ? 0L : FieldRadarTuning.BlockedConfirmMs;
        if (now - PendingSince[b] < confirmMs) return PassState.Unknown;

        State[b] = want;
        Pending[b] = PassState.Unknown;
        return want;
    }

    /// <summary>Two thresholds and a dead band between them. Inside the band the
    /// previous verdict stands — that IS the hysteresis, and it is what makes crossing
    /// a street silent instead of a stream of cues.</summary>
    private static PassState Classify(float clearM, float bodyRadiusM, PassState current)
    {
        if (clearM >= FieldRadarTuning.PassableEnterRadii * bodyRadiusM) return PassState.Passable;
        if (clearM <= BlockedAtM(bodyRadiusM)) return PassState.Blocked;
        return current;
    }

    /// <summary>Where "blocked" begins, in metres — <b>the same boundary the spoken
    /// readout uses</b>, which is the length of the game's own longest forward feeler
    /// (<see cref="FieldNavSideRays.PublishedReachM"/>, 2.00 m at runtime). The reader
    /// calls a side blocked when anything sits inside that segment; a cue that cut
    /// anywhere else could contradict it, and did.
    ///
    /// <para>The body-radius figure is the fallback for evaluations before the probe
    /// has measured — it is not an alternative definition, and it is set to agree:
    /// four radii is 2.0 m at the capsule this game reports.</para></summary>
    public static float BlockedAtM(float bodyRadiusM)
    {
        float published = FieldNavSideRays.PublishedReachM;
        return published > 0f ? published : FieldRadarTuning.BlockedEnterRadii * bodyRadiusM;
    }

    /// <summary>Forget one beam — it was measured against a world that is no longer
    /// there. The next verdict re-seeds silently.</summary>
    public static void Wipe(RadarBeam beam)
    {
        int b = (int)beam;
        State[b] = PassState.Unknown;
        Pending[b] = PassState.Unknown;
        Seeded[b] = false;
    }

    public static void WipeAll()
    {
        for (int b = 0; b < N; b++) Wipe((RadarBeam)b);
    }
}
