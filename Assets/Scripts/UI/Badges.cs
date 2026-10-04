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
    /// The colour of a badge's glyph: the one blue once earned, as Daylight
    /// draws every earned badge, and muted until then. Amber for the winning
    /// ones and a colour per Trailblazer level made the list a second palette
    /// beside the grid's.
    /// </summary>
    public static Color Colour(string key, bool earned, int tier)
    {
        return ThemeManager.Get(earned ? ThemeRole.AccentText : ThemeRole.TextMuted);
    }

    /// <summary>The disc behind the glyph: the accent's tint once earned, plain grey before.</summary>
    public static Color Wash(Color colour, bool earned)
    {
        return ThemeManager.Get(earned ? ThemeRole.AccentTint : ThemeRole.Fill);
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
