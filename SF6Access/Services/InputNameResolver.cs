using System.Collections.Generic;

namespace SF6Access.Services;

/// <summary>
/// Human-readable names for via.hid input enum values. The key config screen
/// renders assigned buttons/keys as icon glyphs with no text equivalent, so
/// these names are the accessible fallback (gamepad entries cover both
/// PlayStation and Xbox labels).
/// </summary>
public static class InputNameResolver
{
    // via.hid.GamePadButton flag values
    private static readonly Dictionary<uint, string> PadButtonNames = new()
    {
        { 0x1, "D-Pad Up" },
        { 0x2, "D-Pad Down" },
        { 0x4, "D-Pad Left" },
        { 0x8, "D-Pad Right" },
        { 0x10, "Triangle / Y" },
        { 0x20, "Cross / A" },
        { 0x40, "Square / X" },
        { 0x80, "Circle / B" },
        { 0x100, "L1 / LB" },
        { 0x200, "L2 / LT" },
        { 0x400, "R1 / RB" },
        { 0x800, "R2 / RT" },
        { 0x1000, "L3" },
        { 0x2000, "R3" },
        { 0x4000, "Select / Back" },
        { 0x8000, "Start / Options" },
        { 0x10000, "Touchpad / Guide" },
        { 0x100000, "Left Stick Up" },
        { 0x200000, "Left Stick Right" },
        { 0x400000, "Left Stick Down" },
        { 0x800000, "Left Stick Left" },
        { 0x1000000, "Right Stick Up" },
        { 0x2000000, "Right Stick Right" },
        { 0x4000000, "Right Stick Down" },
        { 0x8000000, "Right Stick Left" },
        { 0x10000000, "SL" },
        { 0x20000000, "SR" },
    };

    // via.hid.KeyboardKey values not covered by the generated ranges below
    private static readonly Dictionary<int, string> KeyNames = new()
    {
        { 8, "Backspace" },
        { 9, "Tab" },
        { 13, "Enter" },
        { 16, "Shift" },
        { 17, "Control" },
        { 18, "Alt" },
        { 19, "Pause" },
        { 20, "Caps Lock" },
        { 27, "Escape" },
        { 32, "Space" },
        { 33, "Page Up" },
        { 34, "Page Down" },
        { 35, "End" },
        { 36, "Home" },
        { 37, "Left Arrow" },
        { 38, "Up Arrow" },
        { 39, "Right Arrow" },
        { 40, "Down Arrow" },
        { 44, "Print Screen" },
        { 45, "Insert" },
        { 46, "Delete" },
        { 91, "Left Windows" },
        { 92, "Right Windows" },
        { 93, "Applications" },
        { 106, "Numpad Multiply" },
        { 107, "Numpad Plus" },
        { 108, "Numpad Separator" },
        { 109, "Numpad Minus" },
        { 110, "Numpad Decimal" },
        { 111, "Numpad Divide" },
        { 144, "Num Lock" },
        { 145, "Scroll Lock" },
        { 146, "Numpad Enter" },
        { 160, "Left Shift" },
        { 161, "Right Shift" },
        { 162, "Left Control" },
        { 163, "Right Control" },
        { 164, "Left Alt" },
        { 165, "Right Alt" },
        { 186, "Semicolon" },
        { 187, "Equals" },
        { 188, "Comma" },
        { 189, "Minus" },
        { 190, "Period" },
        { 191, "Slash" },
        { 192, "Grave Accent" },
        { 219, "Left Bracket" },
        { 220, "Backslash" },
        { 221, "Right Bracket" },
        { 222, "Apostrophe" },
        { 223, "OEM 8" },
        { 226, "Backslash (102)" },
    };

    /// <summary>Name a via.hid.GamePadButton value; multi-flag values name each set bit.</summary>
    public static string GamePadButtonName(uint value)
    {
        if (value == 0) return "Unassigned";
        if (PadButtonNames.TryGetValue(value, out var name)) return name;

        var parts = new List<string>();
        for (int bit = 0; bit < 32; bit++)
        {
            uint flag = 1u << bit;
            if ((value & flag) == 0) continue;
            parts.Add(PadButtonNames.TryGetValue(flag, out var n) ? n : $"Button {flag}");
        }
        return parts.Count > 0 ? string.Join(" + ", parts) : value.ToString();
    }

    /// <summary>Name a via.hid.KeyboardKey value.</summary>
    public static string KeyboardKeyName(int value)
    {
        if (value == 0) return "Unassigned";
        if (value >= 48 && value <= 57) return ((char)value).ToString();  // 0-9
        if (value >= 65 && value <= 90) return ((char)value).ToString();  // A-Z
        if (value >= 96 && value <= 105) return $"Numpad {value - 96}";
        if (value >= 112 && value <= 135) return $"F{value - 111}";
        return KeyNames.TryGetValue(value, out var name) ? name : $"Key {value}";
    }
}
