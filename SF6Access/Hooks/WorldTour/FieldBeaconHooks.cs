namespace SF6Access.Hooks.WorldTour;

/// <summary>The established local discovery radius shared by field readers.</summary>
public static class FieldBeaconHooks
{
    // The 25 m local range was calibrated for the original NPC beacon in August 2026.
    internal const float HOME_RANGE_M = 25f;
}
