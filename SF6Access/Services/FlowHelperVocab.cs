namespace SF6Access.Services;

/// <summary>
/// Spoken command vocabulary (directions in numpad notation, motion names,
/// attack icons and input glyphs) — the data half of FlowHelper's
/// SpeakableIcons/SpeakInputToken. The table here is the ENGLISH default;
/// every other game language overrides entries through its SF6Access.lang
/// file (keys "dir.0".."dir.9", "motion.236", "icon.lp", "input.BTL_X"…),
/// so translators can adjust the wording without recompiling.
/// </summary>
public static partial class FlowHelper
{
    private sealed class CommandWords
    {
        public string[] Directions;     // numpad 0-9
        public System.Collections.Generic.Dictionary<string, string> Motions;
        public System.Collections.Generic.Dictionary<string, string> Icons;
        public System.Collections.Generic.Dictionary<string, string> Inputs;
    }

    private static readonly CommandWords WordsEn = new()
    {
        Directions = new[] { "neutral", "down-back", "down", "down-forward", "back",
            "neutral", "forward", "up-back", "up", "up-forward" },
        Motions = new()
        {
            { "236", "quarter circle forward" }, { "214", "quarter circle back" },
            { "623", "forward, down, down-forward" }, { "421", "back, down, down-back" },
            { "41236", "half circle forward" }, { "63214", "half circle back" },
            { "236236", "double quarter circle forward" }, { "214214", "double quarter circle back" },
            { "22", "down, down" }, { "44", "back, back" }, { "66", "forward, forward" },
        },
        Icons = new()
        {
            { "+", "plus" }, { "p", "punch" }, { "k", "kick" },
            { "lp", "light punch" }, { "mp", "medium punch" }, { "hp", "heavy punch" },
            { "lk", "light kick" }, { "mk", "medium kick" }, { "hk", "heavy kick" },
            { "ls", "light attack" }, { "ms", "medium attack" }, { "hs", "heavy attack" },
            { "di", "drive impact" }, { "dp", "drive parry" }, { "tr", "throw" },
            { "sm", "special move" }, { "sa", "super art" }, { "auto", "auto" }, { "n", "neutral" },
        },
        Inputs = new()
        {
            { "BTL_LR_LSX", "left or right" }, { "BTL_UD_LSY", "up or down" },
            { "BTL_PLUS_LS_U", "up" }, { "BTL_PLUS_LS_D", "down" },
            { "BTL_PLUS_LS_L", "left" }, { "BTL_PLUS_LS_R", "right" },
            { "BTL_LS_U", "up" }, { "BTL_LS_D", "down" }, { "BTL_LS_L", "left" }, { "BTL_LS_R", "right" },
            { "BTL_X", "square" }, { "BTL_Y", "triangle" }, { "BTL_A", "cross" }, { "BTL_B", "circle" },
            { "BTL_LB", "L1" }, { "BTL_RB", "R1" }, { "BTL_LT", "L2" }, { "BTL_RT", "R2" },
            { "BTL_L3", "L3" }, { "BTL_R3", "R3" }, { "BTL_LSB", "L3" }, { "BTL_RSB", "R3" },
            { "BTL_U", "up" }, { "BTL_D", "down" }, { "BTL_L", "left" }, { "BTL_R", "right" },
            { "UIDecide", "confirm" }, { "UICancel", "cancel" },
            { "MouseL", "left click" }, { "MouseR", "right click" },
        },
    };

    /// <summary>CommandWords for the current language: the English table with
    /// every entry passed through the language file.</summary>
    private static CommandWords BuildWords()
    {
        var w = new CommandWords
        {
            Directions = new string[WordsEn.Directions.Length],
            Motions = new(),
            Icons = new(),
            Inputs = new(),
        };
        for (int i = 0; i < WordsEn.Directions.Length; i++)
            w.Directions[i] = LangFile.Get($"dir.{i}", WordsEn.Directions[i]);
        foreach (var kv in WordsEn.Motions) w.Motions[kv.Key] = LangFile.Get($"motion.{kv.Key}", kv.Value);
        foreach (var kv in WordsEn.Icons) w.Icons[kv.Key] = LangFile.Get($"icon.{kv.Key}", kv.Value);
        foreach (var kv in WordsEn.Inputs) w.Inputs[kv.Key] = LangFile.Get($"input.{kv.Key}", kv.Value);
        return w;
    }
}
