using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Authoritative combo state read straight from the battle side (app.cTeam).
/// <c>mComboCount</c> is the game's own combo counter: it stays above zero for
/// the WHOLE combo — through hitstun, juggles and long finisher animations —
/// and only returns to zero when the combo truly ends or is dropped. Polling it
/// replaces the quiet-frames / HUD-fade heuristics that mis-fired and announced
/// a combo as finished while a slow move was still part of it.
///
/// A cTeam is reachable from any shell: <c>cWork.owner_add (cPlayer) -> mpTeam</c>,
/// or directly from a player: <c>cPlayer.mpTeam</c>. Callers that only have a
/// cWork/cPlayer at hook time (e.g. combo trials) cache the teams here so the
/// per-frame polls can read the live count without a player reference.
/// </summary>
public static class ComboTracker
{
    private static ManagedObject _teamA; // attacker side
    private static ManagedObject _teamB; // defender side

    /// <summary>cTeam for a shell (cWork): owner_add (cPlayer) -> mpTeam.</summary>
    public static ManagedObject TeamOf(ManagedObject cWork)
    {
        var owner = FlowHelper.GetObjectField(cWork, "owner_add"); // cPlayer
        return FlowHelper.GetObjectField(owner, "mpTeam");          // cTeam
    }

    /// <summary>Cache both sides' teams from a battle hook (attacker shell +
    /// defender player), so later polls can read the live combo state. The
    /// combo counter lives on one side only, so both are kept and checked.</summary>
    public static void NoteTeams(ManagedObject attackerShell, ManagedObject defenderPlayer)
    {
        var a = TeamOf(attackerShell);
        if (a != null) _teamA = a;
        var b = FlowHelper.GetObjectField(defenderPlayer, "mpTeam");
        if (b != null) _teamB = b;
    }

    /// <summary>Live combo count + accumulated damage on a team (0 if no combo).</summary>
    public static int CountOf(ManagedObject team, out int damage)
    {
        damage = 0;
        if (team == null) return 0;
        int count = FlowHelper.ReadShortField(team, "mComboCount");
        if (count <= 0) return 0;
        damage = FlowHelper.ReadIntField(team, "mComboDamage", 0);
        return count;
    }

    /// <summary>Whether a combo is running on either cached team.</summary>
    public static bool IsComboActive()
    {
        int d;
        return CountOf(_teamA, out d) > 0 || CountOf(_teamB, out d) > 0;
    }

    public static void Clear()
    {
        _teamA = null;
        _teamB = null;
    }
}
