namespace SF6Access.Services.WorldTour;

/// <summary>Separates a settled radar verdict from what actually reached the audio channel.</summary>
internal struct RadarCueMemory
{
    internal PassState Pending { get; private set; }
    internal PassState Heard { get; private set; }

    internal void Offer(PassState settled)
    { if (settled != PassState.Unknown) Pending = settled; }

    internal void SuppressPending() => Pending = PassState.Unknown;

    internal PassState Candidate()
    {
        if (Pending == Heard) Pending = PassState.Unknown;
        return Pending;
    }

    internal void Acknowledge(PassState played)
    {
        if (played == PassState.Unknown) return;
        Heard = played;
        Pending = PassState.Unknown;
    }

    internal void AcknowledgeWallTone(bool played, bool currentlyBlocked, PassState settled)
    {
        if (!played || !currentlyBlocked) return;
        Acknowledge(PassState.Blocked);
        // A tone can precede blocked confirmation. Keep its possible recovery pending;
        // current evidence still vetoes the opening while the obstruction remains.
        if (settled == PassState.Passable) Offer(PassState.Passable);
    }
}
