using System;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// A repeating homing sound built from one of the mod's OWN audio files: panned
/// toward the target, dropped an octave when the target is behind, and repeated
/// faster the closer the target gets.
///
/// <para><b>Why a mod file and not a game sound.</b> Wwise can only fire events
/// that already live in SF6's soundbanks, and the ones that carry far enough to
/// be useful are NPC voice lines — a different line every time, attenuated by the
/// engine's own curves, and inaudible past a few metres. A beacon has to be the
/// SAME recognisable sound every time and has to be audible at the distance a
/// mission objective actually sits at, so it is mixed by the mod instead
/// (<see cref="AudioService"/>).</para>
///
/// <para><b>Three channels, one per question.</b> <i>Pan</i> answers "which way
/// do I turn" and is continuous, straight off the bearing, so a small correction
/// is heard as a small move rather than snapping between three positions.
/// <i>Pitch</i> answers "am I facing it at all": a stereo pan genuinely cannot
/// tell front from back — a target dead ahead and one dead behind both sit in the
/// centre — so the octave drop supplies exactly the bit the pan is missing, and
/// it stays a clean binary so it can never be mistaken for anything else.
/// <i>Cadence</i> answers "am I getting closer", which is the question the player
/// would otherwise have to keep asking out loud.</para>
///
/// <para>The frame is the CAMERA's, from <see cref="FieldDirectionService"/> —
/// World Tour movement is camera-relative, so "ahead" must mean "push the stick
/// up". That maths (including the mirrored-handedness fix) is calibrated in game
/// and lives in that service; nothing here recomputes it.</para>
/// </summary>
public sealed class HomingCue
{
    // Playback rate when the target is in the back half of the camera frame:
    // one octave down, the user's own specification.
    private const float BEHIND_RATE = 0.5f;
    private const float AHEAD_RATE = 1f;

    // The repeat interval is measured as the SILENCE BETWEEN repeats, not as a
    // period: the cue lasts as long as the file does, and twice that when it is
    // pitched down, so spacing by the gap is what keeps a repeat from ever
    // landing on top of the one before it, whatever file a caller passes.
    //
    // Near bound: a quarter of a second is about the shortest silence that still
    // reads as two separate pings instead of one stuttering sound.
    private const int GAP_NEAR_MS = 250;
    // Far bound: three seconds. Slow enough to sit under the beacon's spoken
    // line without crowding it, quick enough that a wrong turn is audible within
    // a few steps rather than half a street later.
    private const int GAP_FAR_MS = 3000;

    // Both are UX pacing choices rather than values the game holds, so they are
    // named here and nowhere else; the distances they interpolate between are
    // the caller's, because only the caller knows what "arrived" means for it.

    private readonly string _file;
    private readonly float _tightAtM;
    private readonly float _looseFromM;
    private long _nextTick;

    /// <param name="fileName">Sound file in the mod's sounds folder.</param>
    /// <param name="tightAtM">Distance at which the repeat is at its fastest —
    /// normally the caller's own "arrived" distance, so the cue peaks exactly as
    /// the beacon hands over.</param>
    /// <param name="looseFromM">Distance from which the repeat is at its slowest.
    /// It has to span the whole approach: pinned at its slowest for all but the
    /// last few metres, the cadence would say nothing about progress.</param>
    public HomingCue(string fileName, float tightAtM, float looseFromM)
    {
        _file = fileName;
        _tightAtM = tightAtM;
        _looseFromM = looseFromM;
    }

    /// <summary>True when the next repeat is due. A clock comparison and nothing
    /// else, so a caller can ask it every frame and only pay for the position
    /// reads on the frame that actually sounds.</summary>
    public bool Due => Environment.TickCount64 >= _nextTick;

    /// <summary>Stop repeating. The next <see cref="Sound"/> fires immediately —
    /// re-acquiring a target should be heard at once, not after a gap.</summary>
    public void Reset() => _nextTick = 0;

    /// <summary>Sound one repeat toward the offset <c>(dx, dz)</c> in the given
    /// forward frame, and schedule the next one from <paramref name="dist"/>.</summary>
    public void Sound(FieldDirectionService.FlatDir forward, float dx, float dz, float dist)
    {
        var bearing = FieldDirectionService.GetBearing(forward, dx, dz);
        // No usable frame (camera unreadable, target underfoot): centre and
        // unpitched still beats silence, since the cadence alone carries range.
        float pan = bearing.Ok ? bearing.Right : 0f;
        float rate = bearing.Ok && bearing.Behind ? BEHIND_RATE : AHEAD_RATE;

        var length = AudioService.PlaySound(_file, pan, rate: rate);
        // A cue that did not play (missing file) must not turn into a tight retry
        // loop hammering the log; fall back to the slowest spacing.
        long spacing = length > TimeSpan.Zero
            ? (long)length.TotalMilliseconds + Gap(dist)
            : GAP_FAR_MS;
        _nextTick = Environment.TickCount64 + spacing;

        REFrameworkNET.API.LogInfo(
            $"[SF6Access] Homing cue {_file} at {dist:0.0}m, pan {pan:0.00}, rate {rate:0.0}");
    }

    /// <summary>Silence between repeats: shortest at <c>_tightAtM</c>, longest
    /// from <c>_looseFromM</c> out, straight-line in between.</summary>
    private long Gap(float dist)
    {
        float span = _looseFromM - _tightAtM;
        float t = span <= 0f ? 1f : Math.Clamp((dist - _tightAtM) / span, 0f, 1f);
        return (long)(GAP_NEAR_MS + t * (GAP_FAR_MS - GAP_NEAR_MS));
    }
}
