using UnityEngine;

/// <summary>
/// The eleven badges, in the order every screen shows them.
///
/// Only presentation lives here - the names, the order, the colours. Whether a
/// badge is earned is decided by my_profile() on the server and nowhere else, so
/// the phone and the website can never disagree about it.
/// </summary>
public static class Badges
{
    /// <summary>The server's keys, in display order. Sprites are assigned in this order.</summary>
    public static readonly string[] Keys =
    {
        "first_trail", "guided", "flag_runner", "free_roamer", "scholar",
        "flawless", "trailblazer", "team_player", "captain", "summit", "sharp_eye"
    };

    /// <summary>The ones about winning wear amber rather than the accent.</summary>
    static bool IsGold(string key)
    {
        return key == "flag_runner" || key == "flawless" || key == "summit";
    }

    public static int IndexOf(string key)
    {
        for (int i = 0; i < Keys.Length; i++)
            if (Keys[i] == key)
                return i;

        return -1;
    }

    public static string Name(string key)
    {
        return AppStrings.Get("badge." + key);
    }

    public static string Rule(string key)
    {
        return AppStrings.Get("badge." + key + ".rule");
    }

    /// <summary>
    /// The colour of a badge's ring and glyph.
    ///
    /// Trailblazer is the one with steps, and each step has its own colour so the
    /// three levels read as three different things rather than one badge that got
    /// slightly more filled in: green, amber, purple.
    /// </summary>
    public static Color Colour(string key, bool earned, int tier)
    {
        if (!earned)
            return ThemeManager.Get(ThemeRole.TextMuted);

        if (key == "trailblazer")
        {
            if (tier >= 3)
                return ThemeManager.IsDark ? Hex(0xB294F0) : Hex(0x7A4BC2);

            if (tier == 2)
                return ThemeManager.Get(ThemeRole.StatusFair);

            return ThemeManager.Get(ThemeRole.StatusGood);
        }

        return IsGold(key) ? ThemeManager.Get(ThemeRole.StatusFair)
                           : ThemeManager.Get(ThemeRole.Accent);
    }

    /// <summary>The disc behind the glyph: a wash of the badge's colour, fainter when not yet earned.</summary>
    public static Color Wash(Color colour, bool earned)
    {
        colour.a = earned ? 0.18f : 0.08f;
        return colour;
    }

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f,
                         (rgb & 0xFF) / 255f, 1f);
    }

    /// <summary>"3 / 5", "Level 2", or nothing, for the line under a badge's rule.</summary>
    public static string Progress(ProfileService.BadgeRow row)
    {
        if (row == null)
            return string.Empty;

        if (row.key == "trailblazer")
        {
            string level = row.tier > 0
                ? string.Format(AppStrings.Get("badge.level"), row.tier) : string.Empty;

            if (row.tier >= 3)
                return level;

            string count = row.progress + " / " + row.target;
            return string.IsNullOrEmpty(level) ? count : level + " · " + count;
        }

        if (!row.earned && row.target > 1)
            return row.progress + " / " + row.target;

        return string.Empty;
    }
}
