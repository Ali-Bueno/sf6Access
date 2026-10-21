using System;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The radar's VOICE: it turns a beam's new verdict into one panned sound, and a
/// closing wall into a rising tone. It decides nothing about the world.
///
/// <para><b>There is no dedup here at all any more.</b> There used to be a coalescer,
/// because the cue was an EVENT — a line breaking — and one strafe past a door jamb
/// broke the same beam's line twice a few hundred milliseconds apart. The cue is now a
/// STATE CHANGE (<see cref="FieldRadarClearance"/>), and a state cannot settle twice
/// into the same value without passing through the other one first. Duplicates are not
/// filtered; they are unrepresentable.</para>
///
/// <para>Panning is flat stereo, never 3D. A radar cue is egocentric and categorical:
/// it means "left", not "an object exists at this point", and an HRTF rendering would
/// both confuse front with behind and double-encode distance.</para>
/// </summary>
internal static class FieldRadarCues
{
    private const int N = FieldRadarSense.BEAM_COUNT;
    private const string Source = "radar";
    private static TimeSpan MaximumDuration => FieldCuePolicy.MaximumDuration(FieldCueLane.Geometry);
    private static float Volume => FieldRadarTuning.CueVolume
        * (FieldFeedback.SpeechActive ? FieldCuePolicy.SpeechGain : 1f);

    /// <summary>The approach tone, nearest first: a rising note as the wall closes, so
    /// the direction of the pitch is the direction of the change. Built from
    /// AudioService's equal-temperament constants, not raw frequencies.</summary>
    private static readonly float[] PitchNote =
        { AudioService.NoteLaHigh, AudioService.NoteMi, AudioService.NoteLa };

    /// <summary>The stairs motif: still ASCENDING, because the shape of the sound is
    /// the shape of the thing, but drawn from a register the approach ladder never
    /// touches. It used to be the ladder's own three notes in reverse order, which a
    /// player heard — correctly — as the same sound as a wall closing in.</summary>
    private static readonly float[] StairMotif =
        { AudioService.NoteDoLow, AudioService.NoteFaLow, AudioService.NoteDo };

    /// <summary>Last rounded distance announced per beam, or -1 when the tone is
    /// re-armed. AHC re-arms whenever the beam stops travelling the player's way, so
    /// walking back towards a wall tones again.</summary>
    private static readonly int[] LastRounded = new int[N];

    /// <summary>Speak a beam's new verdict.
    ///
    /// <para><b>There is no dedup left here, and none is needed.</b> The old cue was an
    /// EVENT — a line breaking — which could recur on the same feature within a few
    /// hundred milliseconds, so this method carried a coalescer with a time window and
    /// a spatial arm. The cue is now a STATE CHANGE, and a state cannot settle twice
    /// into the same value without passing through the other one first: a duplicate is
    /// no longer suppressed, it is unrepresentable. That is what makes the doubled
    /// sounds gone rather than filtered.</para></summary>
    public static bool Say(RadarBeam beam, PassState state, float clearM, long now)
    {
        if (!FieldFeedback.CueAvailable(FieldCueLane.Geometry, Source)) return false;
        var duration = AudioService.PlaySound(
            state == PassState.Passable ? FieldRadarTuning.ExitSound : FieldRadarTuning.WallSound,
            FieldRadarSense.Pan(beam), Volume, maximumDuration: MaximumDuration);
        if (duration <= TimeSpan.Zero) return false;
        FieldFeedback.ReserveCue(FieldCueLane.Geometry, Source, duration);
        // The speed is here so the cost of any confirm delay can be MEASURED rather than
        // argued about: metres of travel = speed x delay, and the log now carries both.
        REFrameworkNET.API.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[SF6Access] Cue {0} beam={1} clear={2:F2} m speed={3:F2} m/s t={4}",
            state == PassState.Passable ? "PASSABLE" : "BLOCKED ", beam, clearM,
            FieldRadarTravel.SpeedMs, now));

        return true;
    }

    /// <summary>The stairs motif, panned to its beam.</summary>
    public static bool Stairs(RadarBeam beam)
    {
        if (!FieldFeedback.CueAvailable(FieldCueLane.Geometry, Source)) return false;
        var duration = AudioService.PlayTone(StairMotif, FieldRadarSense.Pan(beam), Volume, MaximumDuration);
        FieldFeedback.ReserveCue(FieldCueLane.Geometry, Source, duration);
        return duration > TimeSpan.Zero;
    }

    /// <summary>The proximity tone, AHC's own rule: while the beam travels roughly the
    /// player's way, a tone on every CHANGE of the ROUNDED contact distance below
    /// <see cref="FieldRadarTuning.PitchReachM"/>
    /// (<c>ReactiveRadar.cs:177-191</c>). Absolute metres — deliberately not a
    /// time-to-contact bucket, which is one of the inventions RE7 removed.
    ///
    /// <para>Returns true when a tone actually played.</para></summary>
    public static bool Pitch(RadarBeam beam, float distanceM)
    {
        int b = (int)beam;
        int rounded = (int)Math.Round(distanceM);
        if (rounded < 0 || rounded >= FieldRadarTuning.PitchReachM) { LastRounded[b] = -1; return false; }
        if (rounded == LastRounded[b]) return false;
        // Geometry can coexist with steering and objects, but cannot pile up its own tones.
        if (!FieldFeedback.CueAvailable(FieldCueLane.Geometry, Source)) return false;

        int step = Math.Clamp(rounded, 0, PitchNote.Length - 1);
        REFrameworkNET.API.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "[SF6Access] Tone beam={0} rounded={1} d={2:F2}", beam, rounded, distanceM));
        // Same level as every other cue: the STEP is carried by the note, never by the
        // loudness — a tone that fades with range says "distance" twice and blurs both.
        var duration = AudioService.PlayTone(new[] { PitchNote[step] }, FieldRadarSense.Pan(beam),
                                             Volume, MaximumDuration);
        if (duration <= TimeSpan.Zero) return false;
        FieldFeedback.ReserveCue(FieldCueLane.Geometry, Source, duration);
        LastRounded[b] = rounded;
        return true;
    }

    /// <summary>Re-arm a beam's approach tone, so walking back towards the same wall
    /// tones again (AHC sets its last rounded distance to MaxValue).</summary>
    public static void RearmPitch(RadarBeam beam) => LastRounded[(int)beam] = -1;

    public static void Reset()
    {
        for (int b = 0; b < N; b++) LastRounded[b] = -1;
    }
}
