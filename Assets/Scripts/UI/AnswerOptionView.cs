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

        /// <summary>Picked, and not yet sent.</summary>
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

    [Tooltip("The border. Carries the state on an unchosen option, where the " +
             "background carries nothing.")]
    public Image edge;

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

    [Header("Daylight v2 radio")]
    [Tooltip("The radio's disc. Its colour is the state.")]
    public Image radio;

    [Tooltip("The hole in the disc: wide for an empty radio, small for a chosen one.")]
    public RectTransform radioHole;
    public Image radioHoleImage;
    public Image radioTick;

    [Tooltip("Dims a muted option as a whole, words and radio together.")]
    public CanvasGroup fade;

    public void Set(State state)
    {
        Color line = ThemeManager.Get(ThemeRole.Line);
        Color accent = ThemeManager.Get(ThemeRole.Accent);
        Color bad = ThemeManager.Get(ThemeRole.BadDot);
        Color good = ThemeManager.Get(ThemeRole.GoodDot);
        Color surface = ThemeManager.Get(ThemeRole.Surface);

        // A hairline round an untouched option; the accent round the chosen one;
        // green or red, on a wash of the same, once it has been judged.
        Color border = state == State.Wrong ? bad
                     : state == State.Right ? good
                     : state == State.Chosen ? accent
                     : line;

        if (background != null)
        {
            background.color = state == State.Right ? ThemeManager.Get(ThemeRole.GoodTint)
                             : state == State.Wrong ? ThemeManager.Get(ThemeRole.BadTint)
                             : surface;
        }

        if (edge != null)
            edge.color = border;

        if (radio != null)
        {
            radio.color = state == State.Right ? good
                        : state == State.Wrong ? bad
                        : state == State.Chosen ? accent
                        : ThemeManager.Get(ThemeRole.FillStrong);
        }

        // 22px disc: a 2px ring when empty, a 7px ring when chosen or wrong, and
        // solid with a tick when right.
        if (radioHole != null)
        {
            bool hole = state != State.Right;
            radioHole.gameObject.SetActive(hole);
            float size = state == State.Chosen || state == State.Wrong ? 22f : 50f;
            radioHole.sizeDelta = new Vector2(size, size);
        }

        if (radioHoleImage != null)
            radioHoleImage.color = state == State.Wrong ? ThemeManager.Get(ThemeRole.BadTint) : surface;

        if (radioTick != null)
            radioTick.gameObject.SetActive(state == State.Right);

        if (marker != null && radio == null)
            marker.color = border;

        if (label != null)
            label.color = ThemeManager.Get(ThemeRole.TextPrimary);

        // The cross sits at the end of a wrong option; a right one is said by
        // its radio.
        if (verdictIcon != null)
            verdictIcon.SetActive(state == State.Wrong);

        if (state == State.Wrong && verdictImage != null)
        {
            verdictImage.sprite = crossSprite;
            verdictImage.color = ThemeManager.Get(ThemeRole.StatusPoor);
        }

        if (fade != null)
            fade.alpha = state == State.Muted ? 0.45f : 1f;

        // Chosen stays pressable, because choosing is no longer sending: a
        // reader who picks the wrong line has to be able to pick another one.
        if (button != null)
            button.interactable = state == State.Idle || state == State.Chosen;
    }

    static Color Fade(Color colour, float alpha)
    {
        colour.a = alpha;
        return colour;
    }
}
