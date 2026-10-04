using System;
using UnityEngine;

/// <summary>
/// Every colour in the app comes from here. Nothing else may hardcode a colour,
/// otherwise the light/dark switch leaves stranded elements behind.
///
/// New roles go on the end. ThemedElement stores its role as a number, so a
/// role inserted in the middle would repaint every element saved before it.
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

    /// <summary>The blue as a fill: buttons, the lens, the user's dot.</summary>
    Accent,
    OnAccent,
    Scrim,

    // Semantic. Deliberately separate from Accent so status never reads as
    // branding or vice versa. These are the text strengths; the *Dot roles
    // below are the brighter ones a dot or a ring can carry.
    StatusGood,
    StatusFair,
    StatusPoor,
    StatusNone,

    /// <summary>
    /// The blue as text or an icon. The same as Accent in the light theme; in the
    /// dark one the fill blue is too dark to read as text on a dark surface and
    /// the text blue is too light to carry white letters, so they part.
    /// </summary>
    AccentText,

    /// <summary>The quiet fill of a secondary button or a chip: ink at a few per cent.</summary>
    Fill,

    /// <summary>The frosted controls that float over the map and the camera.</summary>
    Glass,
    GlassRim,

    GoodDot,
    WarnDot,
    BadDot,
    GoodTint,
    WarnTint,
    BadTint,

    /// <summary>The toast: ink on light, light on dark, so it reads over anything.</summary>
    Toast,
    OnToast,

    /// <summary>A pressed or second-step fill, a little stronger than Fill.</summary>
    FillStrong,

    /// <summary>The accent at a tint, behind your own row or an earned badge.</summary>
    AccentTint
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
    public Color accentText;
    public Color fill;
    public Color glass;
    public Color glassRim;
    public Color goodDot;
    public Color warnDot;
    public Color badDot;
    public Color goodTint;
    public Color warnTint;
    public Color badTint;
    public Color toast;
    public Color onToast;
    public Color fillStrong;
    public Color accentTint;

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
            case ThemeRole.AccentText: return accentText;
            case ThemeRole.Fill: return fill;
            case ThemeRole.Glass: return glass;
            case ThemeRole.GlassRim: return glassRim;
            case ThemeRole.GoodDot: return goodDot;
            case ThemeRole.WarnDot: return warnDot;
            case ThemeRole.BadDot: return badDot;
            case ThemeRole.GoodTint: return goodTint;
            case ThemeRole.WarnTint: return warnTint;
            case ThemeRole.BadTint: return badTint;
            case ThemeRole.Toast: return toast;
            case ThemeRole.OnToast: return onToast;
            case ThemeRole.FillStrong: return fillStrong;
            case ThemeRole.AccentTint: return accentTint;
            default: return Color.magenta; // loud on purpose - means a role was missed
        }
    }
}

/// <summary>
/// The Daylight palettes (the v2 prototype, 3 October): one blue, near-black ink,
/// white cards on a parchment ground, and true black with grouped greys at night.
/// </summary>
public static class AppTheme
{
    static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color colour);
        return colour;
    }

    static Color Hex(string hex, float alpha)
    {
        Color c = Hex(hex);
        c.a = alpha;
        return c;
    }

    public static Palette Dark()
    {
        // Measured on Surface (#1C1C1E): ink 15.6:1, ink-2 11.8:1, muted 6.0:1,
        // the text blue 6.4:1; white on the fill blue 4.7:1.
        return new Palette
        {
            background = Hex("#000000"),
            surface = Hex("#1C1C1E"),
            surfaceAlt = Hex("#2C2C2E"),
            line = new Color(1f, 1f, 1f, 0.12f),
            textPrimary = Hex("#F5F5F7"),
            textSecondary = Hex("#D1D1D6"),
            textMuted = Hex("#98989D"),
            accent = Hex("#0071E3"),
            onAccent = Hex("#FFFFFF"),
            scrim = new Color(0f, 0f, 0f, 0.5f),
            statusGood = Hex("#30D158"),
            statusFair = Hex("#FFB340"),
            statusPoor = Hex("#FF6961"),
            statusNone = Hex("#98989D"),
            accentText = Hex("#2997FF"),
            fill = new Color(1f, 1f, 1f, 0.08f),
            glass = Hex("#1E1E20", 0.84f),
            glassRim = new Color(1f, 1f, 1f, 0.12f),
            goodDot = Hex("#30D158"),
            warnDot = Hex("#FF9F0A"),
            badDot = Hex("#FF453A"),
            goodTint = Hex("#30D158", 0.16f),
            warnTint = Hex("#FF9F0A", 0.18f),
            badTint = Hex("#FF453A", 0.16f),
            toast = Hex("#F5F5F7"),
            onToast = Hex("#1D1D1F"),
            fillStrong = new Color(1f, 1f, 1f, 0.14f),
            accentTint = Hex("#2997FF", 0.16f)
        };
    }

    public static Palette Light()
    {
        // Measured on white: ink 16.8:1, ink-2 10.1:1, muted 5.1:1 (4.6:1 on the
        // parchment ground), the blue 5.6:1; white on the blue 5.6:1.
        return new Palette
        {
            background = Hex("#F5F5F7"),
            surface = Hex("#FFFFFF"),
            surfaceAlt = Hex("#F5F5F7"),
            line = new Color(0f, 0f, 0f, 0.09f),
            textPrimary = Hex("#1D1D1F"),
            textSecondary = Hex("#424245"),
            textMuted = Hex("#6E6E73"),
            accent = Hex("#0066CC"),
            onAccent = Hex("#FFFFFF"),
            scrim = new Color(0f, 0f, 0f, 0.30f),
            statusGood = Hex("#1B7A36"),
            statusFair = Hex("#A35200"),
            statusPoor = Hex("#C4001A"),
            statusNone = Hex("#6E6E73"),
            accentText = Hex("#0066CC"),
            fill = new Color(0f, 0f, 0f, 0.05f),
            glass = Hex("#FFFFFF", 0.86f),
            glassRim = Hex("#FFFFFF", 0.9f),
            goodDot = Hex("#34C759"),
            warnDot = Hex("#FF9F0A"),
            badDot = Hex("#FF3B30"),
            goodTint = Hex("#34C759", 0.14f),
            warnTint = Hex("#FF9F0A", 0.14f),
            badTint = Hex("#FF3B30", 0.10f),
            toast = Hex("#1D1D1F"),
            onToast = Hex("#FFFFFF"),
            fillStrong = new Color(0f, 0f, 0f, 0.09f),
            accentTint = Hex("#0066CC", 0.10f)
        };
    }
}
