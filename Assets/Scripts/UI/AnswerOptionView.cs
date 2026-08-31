using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One answer a reader can pick.
///
/// Four states, and the reason there are four rather than two is that a wrong
/// answer has to be visible without the right one becoming visible with it. The
/// option the reader chose is marked Wrong; every other option goes back to Idle.
/// Nothing here ever knows which one was correct, because nothing here is ever
/// told - the verdict that comes back from the server says only whether the answer
/// sent was accepted.
/// </summary>
public class AnswerOptionView : MonoBehaviour
{
    public enum State
    {
        Idle,

        /// <summary>Picked, and the check is in flight.</summary>
        Chosen,

        /// <summary>Picked, and the server said no.</summary>
        Wrong,

        /// <summary>Picked, and the server said yes.</summary>
        Right,

        /// <summary>Not answerable right now - locked out, or already finished.</summary>
        Muted
    }

    public Button button;
    public Image background;
    public Image marker;
    public TextMeshProUGUI label;

    [Tooltip("The tick or cross. Off unless the answer has been judged.")]
    public GameObject verdictIcon;

    public Image verdictImage;
    public Sprite tickSprite;
    public Sprite crossSprite;

    public int Index { get; private set; }

    public void Bind(int index, string text)
    {
        Index = index;

        if (label != null)
            label.text = text;

        Set(State.Idle);
    }

    public void Set(State state)
    {
        Color line = ThemeManager.Get(ThemeRole.Line);
        Color accent = ThemeManager.Get(ThemeRole.Accent);
        Color bad = ThemeManager.Get(ThemeRole.StatusPoor);
        Color good = ThemeManager.Get(ThemeRole.StatusGood);

        Color edge = state == State.Wrong ? bad
                   : state == State.Right ? good
                   : state == State.Chosen ? accent
                   : line;

        if (background != null)
            background.color = ThemeManager.Get(ThemeRole.SurfaceAlt);

        if (marker != null)
            marker.color = state == State.Muted ? Fade(line, 0.5f) : edge;

        if (label != null)
        {
            label.color = state == State.Muted
                ? ThemeManager.Get(ThemeRole.TextMuted)
                : ThemeManager.Get(ThemeRole.TextPrimary);
        }

        bool judged = state == State.Wrong || state == State.Right;

        if (verdictIcon != null)
            verdictIcon.SetActive(judged);

        if (judged && verdictImage != null)
        {
            verdictImage.sprite = state == State.Right ? tickSprite : crossSprite;
            verdictImage.color = state == State.Right ? good : bad;
        }

        if (button != null)
            button.interactable = state == State.Idle;
    }

    static Color Fade(Color colour, float alpha)
    {
        colour.a = alpha;
        return colour;
    }
}
