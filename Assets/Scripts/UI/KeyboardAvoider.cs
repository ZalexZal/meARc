using UnityEngine;

/// <summary>
/// Lifts a bottom-anchored card clear of the on-screen keyboard.
///
/// The code card sits on the bottom edge because that is where a thumb is. The
/// keyboard arrives in the same place and covers it - including the six boxes,
/// which are the one thing the reader needs to watch while typing into them. A
/// card you type into blind is worse than a card in the middle of the screen.
///
/// Only while the keyboard is actually up. The rest of the time the card stays
/// where it was built, so nothing about the resting layout changes.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class KeyboardAvoider : MonoBehaviour
{
    [Tooltip("The card to move. Defaults to this object.")]
    public RectTransform target;

    [Tooltip("Left between the card and the top of the keyboard.")]
    public float gap = 24f;

    [Tooltip("Units per second. Fast enough to keep up with the keyboard sliding " +
             "in, slow enough that it reads as movement rather than a jump.")]
    public float speed = 4000f;

    [Tooltip("Used when the platform says a keyboard is up but will not say how " +
             "big it is, which some Android builds do. A fraction of the screen.")]
    [Range(0.2f, 0.6f)]
    public float assumedHeight = 0.42f;

    Canvas _canvas;
    float _rest;
    float _lift;
    bool _known;

    void OnEnable()
    {
        if (target == null)
            target = (RectTransform)transform;

        _canvas = GetComponentInParent<Canvas>();

        // Captured on the way in rather than at build time, so this works whatever
        // the card's resting position turns out to be.
        if (!_known)
        {
            _rest = target.anchoredPosition.y;
            _known = true;
        }

        _lift = 0f;
        Apply();
    }

    void OnDisable()
    {
        _lift = 0f;
        Apply();
    }

    void Update()
    {
        float wanted = Mathf.MoveTowards(_lift, Wanted(), speed * Time.unscaledDeltaTime);

        if (Mathf.Approximately(wanted, _lift))
            return;

        _lift = wanted;
        Apply();
    }

    /// <summary>How far up the card needs to be, in canvas units.</summary>
    float Wanted()
    {
        if (!TouchScreenKeyboard.visible)
            return 0f;

        // The canvas is scaled, so the keyboard's height in screen pixels is not
        // the distance to move a rect. scaleFactor is pixels per canvas unit.
        float scale = _canvas != null && _canvas.scaleFactor > 0f
            ? _canvas.scaleFactor
            : 1f;

        float pixels = TouchScreenKeyboard.area.height;

        // Some Android versions report an empty rect while the keyboard is sliding
        // in, and a few never report one at all. Guessing high is the safe way to
        // be wrong: a card lifted too far is readable, and one lifted too little
        // is the bug this exists to fix.
        if (pixels <= 1f)
            pixels = Screen.height * assumedHeight;

        return pixels / scale + gap;
    }

    void Apply()
    {
        if (target == null)
            return;

        Vector2 position = target.anchoredPosition;
        position.y = _rest + _lift;
        target.anchoredPosition = position;
    }
}
