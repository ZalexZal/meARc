using System;
using UnityEngine;

/// <summary>
/// Every colour in the app comes from here. Nothing else may hardcode a colour,
/// otherwise the light/dark switch leaves stranded elements behind.
/// </summary>
public enum ThemeRole
{
    Background,
    Surface,
    SurfaceAlt,
    Line,
    TextPrimary,
    TextSecondary,
    TextMuted,
    Accent,
    OnAccent,
    Scrim,

    // Semantic - reserved for calibration state. Deliberately separate from Accent
    // so status never reads as branding or vice versa.
    StatusGood,
    StatusFair,
    StatusPoor,
    StatusNone
}

[Serializable]
public struct Palette
{
    public Color background;
    public Color surface;
    public Color surfaceAlt;
    public Color line;
    public Color textPrimary;
    public Color textSecondary;
    public Color textMuted;
    public Color accent;
    public Color onAccent;
    public Color scrim;
    public Color statusGood;
    public Color statusFair;
    public Color statusPoor;
    public Color statusNone;

    public Color Get(ThemeRole role)
    {
        switch (role)
        {
            case ThemeRole.Background: return background;
            case ThemeRole.Surface: return surface;
            case ThemeRole.SurfaceAlt: return surfaceAlt;
            case ThemeRole.Line: return line;
            case ThemeRole.TextPrimary: return textPrimary;
            case ThemeRole.TextSecondary: return textSecondary;
            case ThemeRole.TextMuted: return textMuted;
            case ThemeRole.Accent: return accent;
            case ThemeRole.OnAccent: return onAccent;
            case ThemeRole.Scrim: return scrim;
            case ThemeRole.StatusGood: return statusGood;
            case ThemeRole.StatusFair: return statusFair;
            case ThemeRole.StatusPoor: return statusPoor;
            case ThemeRole.StatusNone: return statusNone;
            default: return Color.magenta; // loud on purpose - means a role was missed
        }
    }
}

public static class AppTheme
{
    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color colour);
        return colour;
    }

    /// <summary>
    /// Dark is the default: the Camera tab sits over a live feed, where a light
    /// chrome would glare at night and wash out in daylight.
    /// </summary>
    public static Palette Dark()
    {
        return new Palette
        {
            // Apple's dark system: true black, grouped surfaces one step up,
            // and the one blue. Measured: muted text is 5.8:1 on Surface and
            // 4.8:1 on SurfaceAlt; the blue is 4.6:1 as text on Surface.
            background = Hex("#000000"),
            surface = Hex("#1C1C1E"),
            surfaceAlt = Hex("#2C2C2E"),
            line = Hex("#38383A"),
            textPrimary = Hex("#F5F5F7"),
            textSecondary = Hex("#AEAEB2"),
            textMuted = Hex("#98989D"),
            accent = Hex("#0A84FF"),
            onAccent = Hex("#FFFFFF"),
            scrim = new Color(0f, 0f, 0f, 0.55f),
            statusGood = Hex("#74D96B"),
            statusFair = Hex("#E0A040"),
            statusPoor = Hex("#F0736A"),
            statusNone = Hex("#5EAEEA")
        };
    }

    public static Palette Light()
    {
        return new Palette
        {
            // Parchment ground, white cards, near-black ink, Action Blue.
            // Muted text is 5.4:1 on white, 5.0:1 on parchment and 4.6:1 on
            // SurfaceAlt; white on the blue is 5.6:1.
            background = Hex("#F5F5F7"),
            surface = Hex("#FFFFFF"),
            surfaceAlt = Hex("#EDEDF0"),
            line = Hex("#D2D2D7"),
            textPrimary = Hex("#1D1D1F"),
            textSecondary = Hex("#48484C"),
            textMuted = Hex("#6A6A6F"),
            accent = Hex("#0066CC"),
            onAccent = Hex("#FFFFFF"),
            scrim = new Color(0f, 0f, 0f, 0.35f),
            statusGood = Hex("#41762B"),
            statusFair = Hex("#A96400"),
            statusPoor = Hex("#B3261E"),
            statusNone = Hex("#1F6FB2")
        };
    }
}
