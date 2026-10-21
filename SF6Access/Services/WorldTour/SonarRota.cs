#nullable enable
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The world sonar's arithmetic: which of the nearest things takes the next (single) cue,
/// and how loud and how often a cue plays for its distance.
/// </summary>
public static class SonarRota
{
    /// <summary>Index (into a nearest-first list of <paramref name="count"/>) that plays on
    /// <paramref name="turn"/>, rotating over at most <paramref name="voices"/>; -1 when empty.</summary>
    public static int Pick(int count, int voices, int turn)
    {
        int pool = System.Math.Min(count, voices);
        return pool <= 0 ? -1 : ((turn % pool) + pool) % pool;
    }

    /// <summary>Rotate over the nearest representative of every kind, so repeated props cannot hide accesses.</summary>
    public static int PickKind(IReadOnlyList<int> nearestKinds, int turn)
    {
        var kinds = new HashSet<int>();
        var indices = new List<int>();
        for (int i = 0; i < nearestKinds.Count; i++)
            if (kinds.Add(nearestKinds[i])) indices.Add(i);
        int pick = Pick(indices.Count, indices.Count, turn);
        return pick < 0 ? -1 : indices[pick];
    }

    /// <summary>Loudest at <paramref name="nearM"/> or closer, falling linearly to
    /// <paramref name="quiet"/> at <paramref name="rangeM"/> — never silent while in range.</summary>
    public static float Volume(float distM, float nearM, float rangeM, float loud, float quiet)
        => loud + Along(distM, nearM, rangeM) * (quiet - loud);

    /// <summary>Silence after a cue: <paramref name="nearMs"/> at <paramref name="nearM"/> or
    /// closer, growing linearly to <paramref name="farMs"/> at <paramref name="rangeM"/> and
    /// beyond — repeating faster as the thing gets closer, like the homing beacons.</summary>
    public static long Gap(float distM, float nearM, float rangeM, long nearMs, long farMs)
        => nearMs + (long)System.Math.Round(Along(distM, nearM, rangeM) * (farMs - nearMs));

    /// <summary>0 at <paramref name="nearM"/> or closer, 1 at <paramref name="rangeM"/> or beyond.</summary>
    private static float Along(float distM, float nearM, float rangeM)
        => rangeM <= nearM ? 0f : System.Math.Clamp((distM - nearM) / (rangeM - nearM), 0f, 1f);
}
