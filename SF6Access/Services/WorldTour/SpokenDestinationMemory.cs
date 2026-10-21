#nullable enable
using System;

namespace SF6Access.Services.WorldTour;

internal enum SpokenCandidateState { Current, Pending, Invalid }

/// <summary>The last spoken target owns R3 until it is replaced, left behind, or explicitly cleared.</summary>
internal sealed class SpokenDestinationMemory
{
    internal readonly record struct Check(SpokenCandidateState State, Destination Destination, string Reason);
    private Destination _destination;
    private Func<bool, Check>? _validate;
    private string _reason = "no-spoken-candidate";

    internal void Offer(Destination destination, Func<bool> isCurrent)
        => OfferTracked(destination, taking =>
        {
            bool current = isCurrent();
            return new(current ? SpokenCandidateState.Current : SpokenCandidateState.Invalid,
                destination, current ? "current" : "not-current");
        });

    internal void OfferTracked(Destination destination, Func<bool, Check> validate)
    { _destination = destination; _validate = validate; _reason = "current"; }

    internal void DiscardInvalid() => Validate(taking: false);

    private bool Validate(bool taking)
    {
        if (_validate == null) return false;
        Check check;
        try { check = _validate(taking); }
        catch (Exception ex) { _reason = "unreadable:" + ex.GetType().Name; return false; }
        _reason = check.Reason;
        if (check.State == SpokenCandidateState.Pending) return false;
        if (check.State == SpokenCandidateState.Invalid)
        { _validate = null; return false; }
        _destination = check.Destination;
        return true;
    }

    internal bool TryTake(out Destination destination) => TryTake(out destination, out string ignored);

    internal bool TryTake(out Destination destination, out string reason)
    {
        bool valid = Validate(taking: true);
        destination = _destination;
        reason = _reason;
        // A temporary read failure is retryable; it must neither select stale coordinates
        // nor erase the target the player just heard.
        if (valid) Clear();
        return valid;
    }

    internal void Clear() { _destination = default; _validate = null; _reason = "no-spoken-candidate"; }
}
