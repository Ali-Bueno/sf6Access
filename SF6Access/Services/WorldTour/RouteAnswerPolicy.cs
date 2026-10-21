namespace SF6Access.Services.WorldTour;

internal enum RouteAnswerStatus { Pending, Unknown, Unreachable, Confirmed }

/// <summary>Only a readable route with a usable corner authorizes walking guidance.</summary>
internal static class RouteAnswerPolicy
{
    internal static RouteAnswerStatus Classify(bool answered, bool exists, bool throttled,
        bool startOffMesh, bool readableCorner)
        => throttled || answered && startOffMesh ? RouteAnswerStatus.Pending
            : !answered || exists && !readableCorner ? RouteAnswerStatus.Unknown
            : exists ? RouteAnswerStatus.Confirmed : RouteAnswerStatus.Unreachable;

    internal static string WalkingState(bool viaRoute, bool isApproach, bool atFoot, bool near)
        => atFoot ? "approach-near" : !viaRoute ? "unavailable"
            : isApproach ? (near ? "approach-near" : "approach")
            : near ? "near" : "route";
}
