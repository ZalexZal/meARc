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

    public void Set(State state)
    {
        Color line = ThemeManager.Get(ThemeRole.Line);
        Color accent = ThemeManager.Get(ThemeRole.Accent);
        Color bad = ThemeManager.Get(ThemeRole.StatusPoor);
        Color good = ThemeManager.Get(ThemeRole.StatusGood);

        Color border = state == State.Wrong ? bad
                     : state == State.Right ? good
                     : state == State.Chosen ? accent
                     : state == State.Muted ? Fade(line, 0.5f)
                     : line;

        // Outlined, not filled.
        //
        // Four grey slabs stacked down a white sheet are heavy, and being chosen
        // showed up as a small ring changing colour inside one of them. Now an
        // unchosen option is a thin border on the sheet's own ground and the
        // chosen one is filled in the accent, which makes the choice the loudest
        // thing on the screen rather than the quietest.
        //
        // The background keeps its raycast either way: it is the button's target
        // graphic, and an invisible one still has to be pressable.
        if (background != null)
            background.color = state == State.Chosen ? accent : Fade(accent, 0f);

        if (edge != null)
            edge.color = border;

        bool onAccent = state == State.Chosen;

        if (marker != null)
            marker.color = onAccent ? ThemeManager.Get(ThemeRole.OnAccent) : border;

        if (label != null)
        {
            label.color = onAccent ? ThemeManager.Get(ThemeRole.OnAccent)
                        : state == State.Muted ? ThemeManager.Get(ThemeRole.TextMuted)
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
