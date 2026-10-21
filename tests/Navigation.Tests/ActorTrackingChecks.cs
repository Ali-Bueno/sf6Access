using SF6Access.Services.WorldTour;

internal static class ActorTrackingChecks
{
    public static void Run(Action<bool, string> check)
    {
        var actor = new DestinationActor(DestinationActorSource.Avatar, 11, 111, 400, "Kai", 0);
        var selected = new Destination("Kai", null, DestinationCategory.People, 10, 0, 5, Actor: actor);
        var tracking = new ActorTrackingState(selected);
        tracking.Observe(new(true, actor, 20, 1, 6));
        check(tracking.Current.X == 20 && tracking.Current.Y == 1 && tracking.Current.Actor == actor,
            "selected NPC follows live movement while preserving its identity");
        tracking.Observe(default);
        check(tracking.Pending && !tracking.Lost && tracking.Current.X == 20,
            "unreadable actor data holds the last position without declaring loss");
        tracking.Observe(new(true, actor, 21, 1, 6, true));
        check(!tracking.Pending && tracking.InContact && tracking.Current.X == 21,
            "a fresh exact-identity observation restores guidance and real contact");
        tracking.Observe(new(true, actor with { Kind = -1 }, 100, 0, 1, true));
        check(tracking.Pending && !tracking.Lost && !tracking.InContact && tracking.Current.X == 21,
            "temporarily unreadable avatar contact kind holds guidance without declaring a loaded actor lost");
        tracking.Observe(new(true, actor, 22, 1, 6));
        check(!tracking.Pending && !tracking.Lost && tracking.Current.X == 22,
            "restored classification resumes the same chosen NPC without reselecting");
        tracking.Observe(new(true, actor, 21, 1, 6));
        tracking.Observe(new(true, actor, float.NaN, 0, 1));
        check(tracking.Pending && !tracking.Lost && !tracking.InContact && tracking.Current.X == 21,
            "invalid live coordinates suspend guidance and contact without replacing a valid point");
        tracking.Observe(new(true, default));
        check(tracking.Lost && !tracking.Pending && tracking.Current.X == 21,
            "confirmed disappearance preserves the last known position and latches lost");
        tracking.Observe(new(true, actor, 30, 0, 5, true));
        check(tracking.Lost && !tracking.InContact && tracking.Current.X == 21,
            "a recycled or returning instance cannot silently reacquire a lost selection");
        tracking = new ActorTrackingState(selected);
        tracking.Observe(new(true, actor, 30, 0, 5));
        check(!tracking.Lost && tracking.Current.X == 30,
            "deliberately reselecting an actor starts a fresh tracking lifetime");

        foreach (var impostor in new[]
        {
            actor with { Address = 12 }, actor with { Owner = 112 }, actor with { City = 401 },
            actor with { Name = "Other name" }, actor with { Kind = 1 },
        })
        {
            tracking = new ActorTrackingState(selected);
            tracking.Observe(new(true, impostor, 50, 0, 5));
            check(tracking.Lost && tracking.Current.X == 10,
                "changed actor identity never redirects guidance to a different person");
        }

        var other = selected with { X = 12, Actor = actor with { Address = 12, Owner = 112 } };
        var registryCopy = selected with { Actor = actor with { Source = DestinationActorSource.Registry, Address = 22 } };
        var groups = DestinationPicker.Build(new[] { selected, other, registryCopy }, 0, 0, 8);
        check(groups.Single().Items.Count == 2,
            "same-named neighbors stay distinct while avatar and registry views of one actor merge");
        var picker = new DestinationPicker();
        picker.Load(groups, DestinationCategory.People);
        picker.MoveItem(1);
        var movedSelected = selected with { X = 11 };
        var movedOther = other with { X = 50 };
        picker.Load(DestinationPicker.Build(new[] { movedOther, movedSelected }, 0, 0, 8), DestinationCategory.People);
        check(picker.Current.Actor == other.Actor,
            "picker memory follows the exact person even when a namesake moves into their former position");
        check(!default(DestinationActor).IsValid && !(actor with { City = 0 }).IsValid,
            "an actor without complete live identity remains an ordinary static destination");

        tracking = new ActorTrackingState(selected);
        tracking.Observe(new(true, actor with { Kind = -1, Owner = 222 }, 30, 0, 5));
        check(tracking.Lost && !tracking.Pending,
            "unknown contact kind cannot disguise a confirmed change of actor owner");
        var fallbackActor = actor with { Kind = -1 };
        tracking = new ActorTrackingState(selected with { Actor = fallbackActor });
        tracking.Observe(new(true, fallbackActor, 35, 0, 5));
        check(!tracking.Pending && !tracking.Lost && tracking.Current.X == 35,
            "NPCs deliberately selected from the context-name fallback remain trackable");
    }
}
