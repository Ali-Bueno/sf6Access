namespace SF6Access.Services.WorldTour;

/// <summary>Fresh actor observations preserve identity, hold during unreadable data, and latch confirmed loss.</summary>
public sealed class ActorTrackingState
{
    public readonly record struct Observation(bool Readable, DestinationActor Actor,
        float X = 0f, float Y = 0f, float Z = 0f, bool InContact = false);

    public Destination Current { get; private set; }
    public bool Lost { get; private set; }
    public bool Pending { get; private set; }
    public bool InContact { get; private set; }

    public ActorTrackingState(Destination destination) { Current = destination; }

    public void Observe(Observation observation)
    {
        if (Lost) return;
        InContact = false;
        if (!observation.Readable) { Pending = true; return; }
        if (ClassificationIncomplete(Current.Actor, observation.Actor)) { Pending = true; return; }
        Pending = false;
        if (!Current.Actor.IsValid || observation.Actor != Current.Actor) { Lost = true; return; }
        var updated = Current with { X = observation.X, Y = observation.Y, Z = observation.Z };
        if (!updated.HasPosition) { Pending = true; return; }
        Current = updated;
        InContact = observation.InContact;
    }

    /// <summary>An exact instance losing its known contact kind needs another classification read.</summary>
    public static bool ClassificationIncomplete(DestinationActor expected, DestinationActor observed) =>
        expected.Kind >= 0 && observed.Kind < 0 && expected.Source == observed.Source
        && expected.Address == observed.Address && expected.Owner == observed.Owner && expected.City == observed.City;
}
