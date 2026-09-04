using UnityEngine;

/// <summary>
/// The colours for anything drawn on top of the map.
///
/// The map is not the app, and the bug this exists to end is treating it as
/// though it were. Tiles come from Stadia in one style - a light one, because
/// that is the style that names the cafes, banks and churches people navigate
/// by - so a pin label, a scale bar or an attribution line coloured from the
/// app palette is measured against the wrong background.
///
/// It failed in both themes at once, which is the tell that it was one mistake
/// and not two. In the light theme the pin plate was Surface, so a white plate
/// landed on a cream map and its near-black text landed among the map's own
/// near-black labels: a name nobody could pick out. In the dark theme the same
/// line put near-white attribution text on that same cream.
///
/// So none of this follows the theme. The map is the lighter thing in both
/// themes, so dark ink and a dark plate read on it in both.
/// </summary>
public static class MapInk
{
    /// <summary>
    /// How much of a tile's own brightness survives.
    ///
    /// Outdoors has no dark variant that keeps its labels, so the dark theme
    /// gets the same tiles turned down rather than a different map - Alidade
    /// Smooth Dark was measured against it on the same tile and has no points
    /// of interest at all: no cafe, no bank, no church, not even the Tundzha
    /// named or drawn blue.
    ///
    /// 0.62 is a floor, not a taste. It takes the ground from 245 to 152, which
    /// stops it reading as a white slab, and leaves the map's labels at 5.2:1
    /// against it. Multiplying preserves the ratio of the linear values but not
    /// the contrast ratio, because of the 0.05 term in it - so this falls away
    /// faster than it looks like it should: 0.50 gives 3.9:1 and 0.34 gives
    /// 2.5:1, both under the 4.5:1 floor for small text. Below 0.62, dimming
    /// stops being a style and starts being damage.
    /// </summary>
    public static Color TileTint
    {
        get
        {
            return ThemeManager.IsDark
                ? new Color(0.60f, 0.62f, 0.66f, 1f) // very slightly cool, to sit with the chrome
                : Color.white;
        }
    }

    /// <summary>The plate behind a label that sits on the map.</summary>
    public static Color Plate { get { return new Color(0.086f, 0.094f, 0.106f, 0.92f); } }

    /// <summary>Text on that plate.</summary>
    public static Color OnPlate { get { return new Color(0.97f, 0.97f, 0.98f, 1f); } }

    /// <summary>
    /// Ink for marks drawn straight onto the map with no plate behind them: the
    /// scale bar and its distance. 5.7:1 on the dimmed dark map, 15:1 on the
    /// light one.
    /// </summary>
    public static Color Ink { get { return new Color(0.13f, 0.14f, 0.16f, 1f); } }

    /// <summary>
    /// The same ink for the attribution, which has to be legible and must not
    /// compete with the map it is crediting.
    /// </summary>
    public static Color MutedInk { get { return new Color(0.13f, 0.14f, 0.16f, 0.66f); } }

    /// <summary>
    /// A scrim over the radar's map, so the dots and the facing arrow stay the
    /// brightest things on a 300-pixel disc.
    ///
    /// Fixed rather than themed. It was Surface at 45%, which darkens in the
    /// dark theme and *brightens* in the light one - so in light mode the thing
    /// meant to calm the map down was washing it out instead.
    /// </summary>
    public static Color RadarScrim { get { return new Color(0f, 0f, 0f, 0.22f); } }
}
