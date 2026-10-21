using System;

namespace SF6Access.Services;

public enum WorldObjectKind { Ladder, Door, Shop, Place, Breakable, Travel }

/// <summary>Short object identities separated by material, attack and rhythm; pitch never removes a rhythmic event.</summary>
public static class WorldObjectSounds
{
    // Fits the shortest object lane (280 ms), leaving the complete pattern intact behind the player.
    private const double DurationSeconds = .26;
    private const float PeakLevel = .8f;
    private const double BehindPitch = .5;
    private const double AttackSeconds = .001, TailSeconds = .008;
    private static readonly float[][] Patterns = BuildPatterns();

    public static CueAudio.Playback Create(WorldObjectKind kind, float pan, float volume, bool behind = false)
    {
        int index = (int)kind * 2 + (behind ? 1 : 0);
        if ((uint)index >= Patterns.Length) throw new ArgumentOutOfRangeException(nameof(kind));
        return CueAudio.Pcm(Patterns[index], pan, volume);
    }

    private static float[][] BuildPatterns()
    {
        var patterns = new float[Enum.GetValues<WorldObjectKind>().Length * 2][];
        foreach (var kind in Enum.GetValues<WorldObjectKind>())
            for (int behind = 0; behind < 2; behind++)
                patterns[(int)kind * 2 + behind] = Build(kind, behind == 1 ? BehindPitch : 1);
        return patterns;
    }

    private static float[] Build(WorldObjectKind kind, double pitch)
    {
        var samples = new float[(int)(DurationSeconds * CueAudio.SampleRate)];
        uint random = 0x6D2B79F5; // Fixed noise makes an object's texture recognizable on every repeat.
        double slowNoise = 0, fastNoise = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)CueAudio.SampleRate;
            random ^= random << 13; random ^= random >> 17; random ^= random << 5;
            double noise = random / (double)uint.MaxValue * 2 - 1;
            slowNoise += .025 * pitch * (noise - slowNoise);
            fastNoise += .24 * pitch * (noise - fastNoise);
            double texture = fastNoise - slowNoise;
            double value = kind switch
            {
                WorldObjectKind.Ladder => Ladder(t, pitch),
                WorldObjectKind.Door => Door(t, pitch, texture),
                WorldObjectKind.Breakable => Breakable(t, texture),
                WorldObjectKind.Shop => Shop(t, pitch, texture),
                WorldObjectKind.Place => Place(t, pitch, slowNoise),
                WorldObjectKind.Travel => Travel(t, pitch, texture),
                _ => 0,
            };
            double envelope = Math.Min(1, Math.Min(t / AttackSeconds, (samples.Length - i - 1) / (TailSeconds * CueAudio.SampleRate)));
            samples[i] = (float)(value * Math.Max(0, envelope));
        }
        float peak = 0;
        foreach (float sample in samples) peak = Math.Max(peak, Math.Abs(sample));
        if (peak > 0)
            for (int i = 0; i < samples.Length; i++) samples[i] *= PeakLevel / peak;
        return samples;
    }

    private static double Ladder(double t, double pitch)
    {
        // Three metallic rung strikes. Inharmonic resonances distinguish them from musical notes.
        return Clink(t, pitch) + Clink(t - .085, pitch) + Clink(t - .17, pitch);
    }

    private static double Clink(double t, double pitch)
    {
        const double length = .062;
        if (t < 0 || t >= length) return 0;
        return Edge(t, length) * Math.Exp(-t / .018)
            * (Sine(1520 * pitch, t) + .58 * Sine(4090 * pitch, t) + .28 * Sine(6730 * pitch, t));
    }

    private static double Door(double t, double pitch, double texture)
    {
        // Two low wooden knocks, spaced more widely than the ladder's three bright strikes.
        double first = Knock(t, pitch, texture), second = Knock(t - .145, pitch, texture);
        return first + second;
    }

    private static double Knock(double t, double pitch, double texture)
    {
        const double length = .075;
        if (t < 0 || t >= length) return 0;
        return Edge(t, length) * Math.Exp(-t / .018)
            * (Sine(145 * pitch, t) + .4 * Sine(231 * pitch, t) + .3 * texture);
    }

    private static double Breakable(double t, double texture)
    {
        // Dry irregular crackle: no held pitch and no regular tap count to resemble doors or rungs.
        double envelope = Math.Exp(-t / .065) + .65 * Burst(t - .043, .015)
            + .5 * Burst(t - .096, .012) + .25 * Burst(t - .157, .016);
        return texture * envelope;
    }

    private static double Shop(double t, double pitch, double texture)
    {
        // A short register mechanism, then one bright coin/bell ring, rather than a two-note beep.
        double latch = t < .035 ? texture * Math.Exp(-t / .01) : 0;
        double ring = t - .065;
        if (ring < 0) return latch;
        return latch + .45 * Edge(ring, DurationSeconds - .065) * Math.Exp(-ring / .065)
            * (Sine(2093 * pitch, ring) + .5 * Sine(5609 * pitch, ring) + .18 * Sine(8121 * pitch, ring));
    }

    private static double Place(double t, double pitch, double breath)
    {
        // One hollow airy swell with a soft attack, contrasting with every percussive object.
        double envelope = Math.Pow(Math.Sin(Math.PI * t / DurationSeconds), 2);
        return envelope * (2.5 * breath + .12 * Sine(196 * pitch, t) + .035 * Sine(588 * pitch, t));
    }

    private static double Travel(double t, double pitch, double texture)
    {
        // A rough rising zip: continuous motion in pitch, never the stairs' discrete note sequence.
        const double startHz = 240, endHz = 1860;
        double phase = 2 * Math.PI * pitch * (startHz * t + (endHz - startHz) * t * t / (2 * DurationSeconds));
        double envelope = Math.Sin(Math.PI * t / DurationSeconds);
        return envelope * (.6 * Math.Sin(phase) + .23 * Math.Sin(3 * phase) + .1 * Math.Sin(5 * phase) + .35 * texture);
    }

    private static double Sine(double hz, double t) => Math.Sin(2 * Math.PI * hz * t);
    private static double Burst(double t, double decay) => t < 0 ? 0 : Math.Exp(-t / decay);
    private static double Edge(double t, double length) => Math.Max(0, Math.Min(1, Math.Min(t / AttackSeconds, (length - t) / .004)));
}
