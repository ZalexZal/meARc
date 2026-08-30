using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Dims a control while it is held down.
///
/// Unity's own Button transition tints its target graphic, which does nothing at
/// all when that graphic is the invisible rectangle used to make a whole row
/// tappable - a nav tab, a list row, a settings row. Those are most of the
/// controls in this app, so most of the app had no press feedback.
///
/// Opacity rather than scale, deliberately: a control that grows under the thumb
/// moves everything around it, and the jitter reads as a glitch rather than as a
/// response.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class TapFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("How far down the opacity goes while held. Visible without making the " +
             "label unreadable mid-press.")]
    [Range(0.2f, 1f)]
    public float pressedAlpha = 0.55f;

    [Tooltip("Seconds to settle back. Fast enough to feel like a response rather " +
             "than an animation.")]
    public float duration = 0.09f;

    [Tooltip("Held down permanently while the control is not interactable. " +
             "Replacing Unity's colour tint removed its disabled look as well as " +
             "its pressed one, so this has to carry both.")]
    [Range(0.2f, 1f)]
    public float disabledAlpha = 0.4f;

    public Selectable target;

    CanvasGroup _group;
    float _from;
    float _to = 1f;
    float _elapsed;
    bool _running;
    bool _held;
    bool _wasDisabled;

    void Awake()
    {
        _group = GetComponent<CanvasGroup>();

        if (target == null)
            target = GetComponent<Selectable>();
    }

    void OnDisable()
    {
        // A control disabled mid-press would otherwise come back still dimmed,
        // and its pointer-up would never arrive.
        _running = false;
        _held = false;
        _wasDisabled = false;

        if (_group != null)
            _group.alpha = 1f;
    }

    void Update()
    {
        if (_group == null)
            return;

        // A disabled control stays dimmed, and no press animation runs on it. The
        // value is written once on the change rather than every frame - a
        // CanvasGroup alpha marks the whole canvas dirty either way.
        bool disabled = target != null && !target.IsInteractable();

        if (disabled)
        {
            if (!_wasDisabled)
            {
                _wasDisabled = true;
                _group.alpha = disabledAlpha;
            }

            _running = false;
            return;
        }

        if (_wasDisabled)
        {
            _wasDisabled = false;
            _group.alpha = 1f;
        }

        if (!_running)
            return;

        _elapsed += Time.unscaledDeltaTime;

        float t = duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / duration);
        _group.alpha = Mathf.Lerp(_from, _to, t);

        if (t >= 1f)
            _running = false;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _held = true;
        Fade(pressedAlpha);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _held = false;
        Fade(1f);
    }

    void Fade(float to)
    {
        if (_group == null)
            return;

        if (target != null && !target.IsInteractable())
            return;

        _from = _group.alpha;
        _to = to;
        _elapsed = 0f;
        _running = true;
    }
}
