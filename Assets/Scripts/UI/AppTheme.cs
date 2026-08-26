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
            // Surface sits a clear step above the background - at the previous
            // spacing the cards were invisible against it on a phone screen.
            background = Hex("#0B0D10"),
            surface = Hex("#1A1F27"),
            surfaceAlt = Hex("#272D37"),
            line = Hex("#343B47"),
            textPrimary = Hex("#F3F5F7"),
            textSecondary = Hex("#A7ADB5"),
            textMuted = Hex("#727982"),
            accent = Hex("#45C4B0"),
            onAccent = Hex("#04201C"),
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
            background = Hex("#F1F1EF"),
            surface = Hex("#FFFFFF"),
            surfaceAlt = Hex("#E8E9E6"),
            line = Hex("#D3D5D1"),
            textPrimary = Hex("#16181B"),
            textSecondary = Hex("#464B52"),
            textMuted = Hex("#7A8088"),
            accent = Hex("#0F7A6B"),
            onAccent = Hex("#FFFFFF"),
            scrim = new Color(0f, 0f, 0f, 0.35f),
            statusGood = Hex("#41762B"),
            statusFair = Hex("#A96400"),
            statusPoor = Hex("#B3261E"),
            statusNone = Hex("#1F6FB2")
        };
    }
}
