using System;

namespace SF6Access.Services;

/// <summary>
/// Turns an RGB color into a spoken, localized color name ("dark red",
/// "rojo oscuro"). The game stores avatar colors as raw HLS/RGB values with
/// NO name table anywhere in the data, so a human vocabulary is unavoidable —
/// the words are the mod's own (documented last resort), localized through
/// LangFile (color.* keys). Thresholds are tuned for speech usefulness, not
/// colorimetric precision.
/// </summary>
public static class ColorNamer
{
    /// <summary>Name for a via.Color packed rgba uint (r = lowest byte).</summary>
    public static string NameRgba(uint rgba) =>
        NameRgb((byte)(rgba & 0xFF), (byte)((rgba >> 8) & 0xFF), (byte)((rgba >> 16) & 0xFF));

    public static string NameRgb(int r, int g, int b)
    {
        RgbToHsl(r, g, b, out float h, out float s, out float l);
        return NameHsl(h, s, l);
    }

    /// <summary>Name from hue (0-360), saturation (0-1), lightness (0-1).</summary>
    public static string NameHsl(float h, float s, float l)
    {
        // Achromatic ends first
        if (l < 0.09f) return LangFile.Get("color.black", "black");
        if (l > 0.93f && s < 0.35f) return LangFile.Get("color.white", "white");
        if (s < 0.10f)
        {
            string gray = LangFile.Get("color.gray", "gray");
            if (l < 0.30f) return Fmt("dark", gray);
            if (l > 0.70f) return Fmt("light", gray);
            return gray;
        }

        string baseName = BaseHueName(h, s, l);

        if (l < 0.28f) return Fmt("dark", baseName);
        if (l > 0.75f) return Fmt("light", baseName);
        if (s < 0.35f) return Fmt("pale", baseName);
        return baseName;
    }

    private static string BaseHueName(float h, float s, float l)
    {
        h = ((h % 360f) + 360f) % 360f;

        // Warm hues that are dark or desaturated read as brown — covers most
        // skin/hair/eye tones (in-game dumps: #4D3F3E skin h≈4 s≈0.11 l≈0.27,
        // #2C4C52 hazel iris h≈51 s≈0.30 l≈0.25 — both are brown to a human,
        // not "dark red"/"dark yellow")
        bool warmHue = h < 55f || h >= 340f;
        if (warmHue && s < 0.35f && l < 0.60f)
            return LangFile.Get("color.brown", "brown");
        if (h >= 15f && h < 50f && (l < 0.55f || s < 0.5f))
            return LangFile.Get("color.brown", "brown");

        if (h < 15f) return LangFile.Get("color.red", "red");
        if (h < 45f) return LangFile.Get("color.orange", "orange");
        if (h < 70f) return LangFile.Get("color.yellow", "yellow");
        if (h < 160f) return LangFile.Get("color.green", "green");
        if (h < 200f) return LangFile.Get("color.cyan", "cyan");
        if (h < 255f) return LangFile.Get("color.blue", "blue");
        if (h < 290f) return LangFile.Get("color.purple", "purple");
        if (h < 335f) return LangFile.Get("color.pink", "pink");
        return LangFile.Get("color.red", "red");
    }

    // Word order differs per language ("dark red" vs "rojo oscuro") — each
    // modifier is a localized format string around the base name.
    private static string Fmt(string modifier, string baseName)
    {
        string fmt = modifier switch
        {
            "dark" => LangFile.Get("color.fmt.dark", "dark {0}"),
            "light" => LangFile.Get("color.fmt.light", "light {0}"),
            _ => LangFile.Get("color.fmt.pale", "pale {0}"),
        };
        try { return string.Format(fmt, baseName); }
        catch { return baseName; }
    }

    private static void RgbToHsl(int r, int g, int b, out float h, out float s, out float l)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = MathF.Max(rf, MathF.Max(gf, bf));
        float min = MathF.Min(rf, MathF.Min(gf, bf));
        float delta = max - min;

        l = (max + min) / 2f;
        if (delta < 0.0001f) { h = 0; s = 0; return; }

        s = delta / (1f - MathF.Abs(2f * l - 1f));

        if (max == rf) h = 60f * (((gf - bf) / delta) % 6f);
        else if (max == gf) h = 60f * ((bf - rf) / delta + 2f);
        else h = 60f * ((rf - gf) / delta + 4f);
        if (h < 0) h += 360f;
    }
}
