#nullable enable
namespace SF6Access.Services.WorldTour;

/// <summary>
/// The access sonar's arithmetic: which of the nearest things takes the next (single) cue,
/// and how loud a cue is for its distance — the one channel distance is encoded on.
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

    /// <summary>Loudest at <paramref name="nearM"/> or closer, falling linearly to
    /// <paramref name="quiet"/> at <paramref name="rangeM"/> — never silent while in range.</summary>
    public static float Volume(float distM, float nearM, float rangeM, float loud, float quiet)
    {
        if (rangeM <= nearM) return loud;
        float t = System.Math.Clamp((distM - nearM) / (rangeM - nearM), 0f, 1f);
        return loud + t * (quiet - loud);
    }
}
