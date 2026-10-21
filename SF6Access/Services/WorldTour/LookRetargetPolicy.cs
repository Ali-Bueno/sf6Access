namespace SF6Access.Services.WorldTour;

internal enum LookTapAction { Aim, Choose, Cancel }

/// <summary>R3 cancels any active guidance before it can select another spoken target.</summary>
internal static class LookRetargetPolicy
{
    internal static LookTapAction Decide(bool hasSelection, bool followingMission, bool hasSpokenCandidate)
        => hasSelection || followingMission ? LookTapAction.Cancel
            : hasSpokenCandidate ? LookTapAction.Choose : LookTapAction.Aim;
}
