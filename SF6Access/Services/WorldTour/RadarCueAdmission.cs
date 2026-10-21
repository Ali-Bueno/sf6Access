namespace SF6Access.Services.WorldTour;

/// <summary>A deferred radar verdict may sound only while the latest measurement still supports it.</summary>
internal static class RadarCueAdmission
{
    internal static bool Supports(PassState pending, bool measured, float clearanceM,
                                  float passableAtM, float blockedAtM)
    {
        if (!measured || !float.IsFinite(clearanceM) || clearanceM < 0
            || !float.IsFinite(passableAtM) || passableAtM <= 0
            || !float.IsFinite(blockedAtM) || blockedAtM <= 0) return false;
        return pending switch
        {
            PassState.Passable => clearanceM >= passableAtM,
            PassState.Blocked => clearanceM <= blockedAtM,
            _ => false,
        };
    }
}
