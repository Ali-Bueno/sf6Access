using System;

namespace SF6Access.Services.WorldTour;

public enum FieldCueLane { Route, Geometry, Focused, Nearby }

/// <summary>Independent bounded voices; one object cannot echo across the focus and nearby lanes.</summary>
public sealed class FieldCuePolicy
{
    // Retain audible steering and surroundings beneath words; an intentionally quieter UX level.
    public const float SpeechGain = 0.6f;

    private readonly long[] _until = new long[Enum.GetValues<FieldCueLane>().Length];
    private readonly string[] _sources = new string[Enum.GetValues<FieldCueLane>().Length];

    // Listening defaults: retain familiar sample attacks, with a short fading tail after pitch shifting.
    public static TimeSpan MaximumDuration(FieldCueLane lane) => TimeSpan.FromMilliseconds(lane switch
    {
        FieldCueLane.Route => 220,
        FieldCueLane.Geometry => 280,
        FieldCueLane.Focused => 360,
        _ => 280,
    });

    public bool Available(long now, FieldCueLane lane, string source)
    {
        int index = (int)lane;
        if ((uint)index >= _until.Length || string.IsNullOrEmpty(source) || now < _until[index]) return false;
        for (int i = 0; i < _until.Length; i++)
            if (now < _until[i] && _sources[i] == source) return false;
        return true;
    }

    public void Reserve(long now, FieldCueLane lane, string source, TimeSpan duration)
    {
        int index = (int)lane;
        if ((uint)index >= _until.Length || string.IsNullOrEmpty(source) || duration <= TimeSpan.Zero) return;
        long milliseconds = (long)Math.Ceiling(duration.TotalMilliseconds);
        _until[index] = Math.Max(_until[index], now + milliseconds);
        _sources[index] = source;
    }
}
