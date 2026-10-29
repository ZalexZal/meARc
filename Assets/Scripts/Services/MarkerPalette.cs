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

        // A stop its creator never gave a colour takes the one default every
        // stop starts with on the website (30 October 2026): the brand blue.
        // Until then it was white.
        return DefaultStopColour;
    }

    /// <summary>The default for a stop with no colour of its own: #0066CC.</summary>
    public static readonly Color DefaultStopColour = new Color(0f, 0x66 / 255f, 0xCC / 255f);

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
    /// A stable colour for something that has an identity but no chosen colour -
    /// a trail in the list, so far.
    ///
    /// Derived from the id, so the same trail is the same colour on every device
    /// and after every reinstall, with nothing stored anywhere. Saturation and
    /// value are fixed and only the hue moves, which keeps every row at roughly the
    /// same weight beside its neighbours instead of one of them shouting.
    /// </summary>
    public static Color ForKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return new Color(0.27f, 0.77f, 0.69f); // the app's own teal

        unchecked
        {
            // FNV-1a rather than string.GetHashCode, which is allowed to differ
            // between runs and platforms - the colour would flicker.
            uint hash = 2166136261u;
            for (int i = 0; i < key.Length; i++)
            {
                hash ^= key[i];
                hash *= 16777619u;
            }

            return Color.HSVToRGB((hash % 360u) / 360f, 0.55f, 0.82f);
        }
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
