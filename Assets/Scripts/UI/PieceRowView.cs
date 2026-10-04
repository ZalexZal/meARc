using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One thing there is to do at a marker: watch this, listen to that, answer the
/// question. A marker with several is a small sequence rather than a page, and
/// this row is what makes the sequence visible before the reader commits to it.
///
/// Daylight v2 draws them as rows of one inset group: an icon in a small box, the
/// thing's name, and under it what kind of thing it is or why it is shut. A done
/// row turns its box green and loses its chevron; a shut one greys its icon and
/// is not pressable.
/// </summary>
public class PieceRowView : MonoBehaviour
{
    public Button button;
    public Image icon;
    public TextMeshProUGUI caption;
    public TextMeshProUGUI titleLabel;

    [Tooltip("The chevron, shown while there is still something here to do.")]
    public GameObject chevron;

    [Tooltip("Kept so older scenes load. Done is said by the box now.")]
    public GameObject tick;

    public Image tickImage;

    [Tooltip("The small rounded box the icon sits in.")]
    public Image box;

    [Tooltip("The hairline above every row but the first.")]
    public GameObject divider;

    public enum Look { Open, Done, Shut }

    public void Bind(Sprite glyph, string captionText, string title, bool done)
    {
        Draw(glyph, title, captionText, done ? Look.Done : Look.Open);
    }

    /// <summary>The row in one of its three looks, with its words.</summary>
    public void Draw(Sprite glyph, string title, string captionText, Look look)
    {
        if (icon != null && glyph != null)
            icon.sprite = glyph;

        if (caption != null)
        {
            caption.text = captionText;
            Paint(caption, look == Look.Done ? ThemeRole.StatusGood : ThemeRole.TextMuted);
            caption.gameObject.SetActive(!string.IsNullOrEmpty(captionText));
        }

        if (titleLabel != null)
            titleLabel.text = title;

        if (chevron != null)
            chevron.SetActive(look == Look.Open);

        if (tick != null)
            tick.SetActive(false);

        Paint(box, look == Look.Done ? ThemeRole.GoodTint : ThemeRole.Surface);
        Paint(icon, look == Look.Done ? ThemeRole.StatusGood
                  : look == Look.Shut ? ThemeRole.TextMuted
                  : ThemeRole.AccentText);

        if (button != null)
            button.interactable = look != Look.Shut;
    }

    /// <summary>
    /// Colours a graphic by role through its ThemedElement, so a theme switch
    /// repaints it the right way rather than to whatever it was built in.
    /// </summary>
    static void Paint(Graphic graphic, ThemeRole role)
    {
        if (graphic == null)
            return;

        ThemedElement themed = graphic.GetComponent<ThemedElement>();
        if (themed != null)
        {
            themed.role = role;
            themed.Apply();
            return;
        }

        graphic.color = ThemeManager.Get(role);
    }

    public void SetFirst(bool first)
    {
        if (divider != null && divider.activeSelf == first)
            divider.SetActive(!first);
    }
}
