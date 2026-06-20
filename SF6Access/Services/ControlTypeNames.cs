namespace SF6Access.Services;

/// <summary>
/// Localized display names for the three control types, shared by every screen
/// that reports them. The index order matches both the game's UI11410.TabInputType
/// (Classic=0, Modern=1, Dynamic=2) and app.EConfigInputType (NORMAL=0 Classic,
/// CASUAL=1 Modern, SUPER_EASY=2 Dynamic).
/// </summary>
public static class ControlTypeNames
{
    private static readonly string[][] Names =
    {
        new[] { "Classic", "Modern", "Dynamic" },     // En
        new[] { "Clásico", "Moderno", "Dinámico" },   // Es
        new[] { "Clássico", "Moderno", "Dinâmico" },  // Pt
    };

    /// <summary>Localized name for a control-type index (0-2), or null if out of range.</summary>
    public static string Resolve(int index)
    {
        if (index < 0 || index > 2) return null;
        return Names[(int)FlowHelper.GetDisplayLang()][index];
    }
}
