namespace SF6Access.Services.WorldTour;

/// <summary>Explicit citywide listening; ordinary nearby exploration remains the default.</summary>
internal static class CityListeningMode
{
    internal static bool Active { get; private set; }
    internal static void Enter() => Active = true;
    internal static void Exit() => Active = false;
}
