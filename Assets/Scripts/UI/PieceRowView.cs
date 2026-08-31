using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One thing there is to do at a marker: watch this, listen to that, answer the
/// question. A marker with several is a small sequence rather than a page, and
/// this row is what makes the sequence visible before the reader commits to it.
/// </summary>
public class PieceRowView : MonoBehaviour
{
    public Button button;
    public Image icon;
    public TextMeshProUGUI caption;
    public TextMeshProUGUI titleLabel;

    [Tooltip("The chevron, shown while there is still something here to do.")]
    public GameObject chevron;

    [Tooltip("The tick, shown once this piece has been played or answered.")]
    public GameObject tick;

    public Image tickImage;

    public void Bind(Sprite glyph, string captionText, string title, bool done)
    {
        if (icon != null && glyph != null)
            icon.sprite = glyph;

        if (caption != null)
            caption.text = captionText;

        if (titleLabel != null)
            titleLabel.text = title;

        if (chevron != null)
            chevron.SetActive(!done);

        if (tick != null)
            tick.SetActive(done);

        if (tickImage != null)
            tickImage.color = ThemeManager.Get(ThemeRole.StatusGood);
    }
}
