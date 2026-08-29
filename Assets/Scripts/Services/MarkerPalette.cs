using UnityEngine;

/// <summary>
/// One answer to "what colour is this marker", shared by the AR cube, the map pin,
/// the dock row and the sheet.
///
/// The colour is chosen by whoever made the trail, so it can land anywhere -
/// including exactly on the app's own teal. <see cref="RingFor"/> exists for that
/// case: a marker is drawn with a ring in the colour that separates it from the
/// map behind it, so a teal pin on a teal-accented map is still a pin and not a
/// smudge.
/// </summary>
public static class MarkerPalette
{
    public static Color Resolve(BuildingData building)
    {
        if (building == null)
            return Color.white;

        // The trail wins, so one marker can be made to stand out without changing
        // what its whole category looks like.
        if (!string.IsNullOrWhiteSpace(building.markerColourHex) &&
            ColorUtility.TryParseHtmlString(building.markerColourHex.Trim(), out Color parsed))
        {
            return parsed;
        }

        if (building.category != null)
            return building.category.markerColour;

        return Color.white;
    }

    /// <summary>
    /// The ring drawn around a pin or dot. White on a dark theme, near-black on a
    /// light one - whichever the marker colour is not.
    ///
    /// This is what carries the separation, not the marker colour, which is why a
    /// marker is still legible when its colour happens to match the accent, the
    /// map, or another marker beside it.
    /// </summary>
    public static Color RingFor(Color markerColour)
    {
        Color light = new Color(1f, 1f, 1f, 1f);
        Color dark = new Color(0.043f, 0.051f, 0.063f, 1f); // #0B0D10, the dark background

        // Pick the ring that contrasts more with the marker itself, so a white
        // marker gets a dark ring rather than disappearing into one.
        return Contrast(markerColour, light) >= Contrast(markerColour, dark) ? light : dark;
    }

    /// <summary>
    /// Text that can be read on top of this colour. A marker colour is arbitrary,
    /// so neither black nor white is always right.
    /// </summary>
    public static Color OnColour(Color background)
    {
        Color light = new Color(1f, 1f, 1f, 1f);
        Color dark = new Color(0.086f, 0.094f, 0.106f, 1f);

        return Contrast(background, light) >= Contrast(background, dark) ? light : dark;
    }

    /// <summary>WCAG relative-luminance contrast ratio, 1:1 to 21:1.</summary>
    public static float Contrast(Color a, Color b)
    {
        float la = Luminance(a);
        float lb = Luminance(b);

        float lighter = Mathf.Max(la, lb);
        float darker = Mathf.Min(la, lb);

        return (lighter + 0.05f) / (darker + 0.05f);
    }

    static float Luminance(Color c)
    {
        return 0.2126f * Channel(c.r) + 0.7152f * Channel(c.g) + 0.0722f * Channel(c.b);
    }

    static float Channel(float v)
    {
        return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
    }
}
