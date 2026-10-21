using System;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>The last actually spoken look target, offered to the existing R3 shortcut.</summary>
public static class FieldPlaceSweepHooks
{
    private static readonly SpokenDestinationMemory Candidate = new();

    internal static void OfferSpoken(Destination destination, Func<bool> isCurrent)
        => Candidate.Offer(destination, isCurrent);

    internal static void OfferTracked(Destination destination, Func<bool, SpokenDestinationMemory.Check> validate)
        => Candidate.OfferTracked(destination, validate);

    public static bool TryTakeCandidate(out Destination place) => Candidate.TryTake(out place);
    internal static bool TryTakeCandidate(out Destination place, out string reason) => Candidate.TryTake(out place, out reason);

    internal static void DiscardInvalid() => Candidate.DiscardInvalid();
    internal static void Clear() => Candidate.Clear();
}
