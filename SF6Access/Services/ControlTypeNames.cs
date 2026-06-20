using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Display names for the three control types, shared by every screen that
/// reports them. Prefers the game's OWN localized string (so it works in any
/// game language); falls back to a hardcoded En/Es/Pt table only if the game
/// lookup is unavailable. Index order matches the game's UI11410.TabInputType
/// (Classic=0, Modern=1, Dynamic=2) and app.EConfigInputType (NORMAL=0 Classic,
/// CASUAL=1 Modern, SUPER_EASY=2 Dynamic).
/// </summary>
public static class ControlTypeNames
{
    // Last-resort fallback when the game lookup fails (English used for any
    // language outside these three — GetDisplayLang only buckets En/Es/Pt).
    private static readonly string[][] Names =
    {
        new[] { "Classic", "Modern", "Dynamic" },     // En
        new[] { "Clásico", "Moderno", "Dinámico" },   // Es
        new[] { "Clássico", "Moderno", "Dinâmico" },  // Pt
    };

    private static Method _dispMessage;
    private static bool _lookedUp;

    /// <summary>Localized name for a control-type index (0-2), or null if out of range.</summary>
    public static string Resolve(int index)
    {
        if (index < 0 || index > 2) return null;

        string fromGame = ResolveFromGame(index);
        if (!string.IsNullOrWhiteSpace(fromGame)) return fromGame;

        return Names[(int)FlowHelper.GetDisplayLang()][index];
    }

    /// <summary>
    /// app.IDScriptExtensions.DispMessage(EConfigInputType) returns the control
    /// type's localized display name in the current game language. EConfigInputType
    /// is sbyte-backed (NORMAL=0/CASUAL=1/SUPER_EASY=2), matching the index, so the
    /// argument is passed as the underlying sbyte (the same way fighter-name lookups
    /// pass CHARA_ID as a byte).
    /// </summary>
    private static string ResolveFromGame(int index)
    {
        try
        {
            if (!_lookedUp)
            {
                _lookedUp = true;
                _dispMessage = TDB.Get().FindType("app.IDScriptExtensions")
                    ?.GetMethod("DispMessage(app.EConfigInputType)");
            }
            if (_dispMessage == null) return null;

            string name = _dispMessage.InvokeBoxed(typeof(string), null, new object[] { (sbyte)index }) as string;
            return FlowHelper.CleanTags(name)?.Trim();
        }
        catch { return null; }
    }
}
