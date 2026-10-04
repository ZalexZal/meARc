using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The press, on every control the builder makes: it shrinks to 95% while held
/// and springs back on release - Daylight's one micro-interaction. Disabled
/// controls are dimmed instead and do not move.
///
/// Scale rather than opacity. Most controls here have an invisible target
/// graphic so a whole row can be tapped, and dimming a row of text read as the
/// text flickering; a press that moves reads as a press. localScale does not
/// touch layout, so nothing around the control shifts.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class TapFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("How small the control gets while held.")]
    [Range(0.8f, 1f)]
    public float pressedScale = 0.95f;

    [Tooltip("Kept so old scenes still load. The press no longer fades.")]
    [Range(0.2f, 1f)]
    public float pressedAlpha = 1f;

    [Tooltip("Seconds to settle back.")]
    public float duration = 0.09f;

    [Tooltip("Held down permanently while the control is not interactable.")]
    [Range(0.2f, 1f)]
    public float disabledAlpha = 0.38f;

    public Selectable target;

    CanvasGroup _group;
    RectTransform _rect;
    bool _held;
    bool _wasDisabled;
    float _scale = 1f;
    float _velocity;

    void Awake()
    {
        _group = GetComponent<CanvasGroup>();
        _rect = transform as RectTransform;

        if (target == null)
            target = GetComponent<Selectable>();
    }

    void OnDisable()
    {
        // A control disabled mid-press would otherwise come back still pressed,
        // and its pointer-up would never arrive.
        _held = false;
        _wasDisabled = false;
        _scale = 1f;
        _velocity = 0f;

        if (_group != null)
            _group.alpha = 1f;

        if (_rect != null)
            _rect.localScale = Vector3.one;
    }

    void Update()
    {
        if (_group == null)
            return;

        bool disabled = target != null && !target.IsInteractable();

        if (disabled != _wasDisabled)
        {
            _wasDisabled = disabled;
            _group.alpha = disabled ? disabledAlpha : 1f;
        }

        if (disabled)
            _held = false;

        float goal = _held ? pressedScale : 1f;

        if (Mathf.Approximately(_scale, goal) && Mathf.Abs(_velocity) < 0.0001f)
            return;

        // Down quickly, up on a short spring with a touch of give: the release
        // is the part that reads as the control answering.
        if (_held)
        {
            _scale = Mathf.MoveTowards(_scale, goal, Time.unscaledDeltaTime / Mathf.Max(0.01f, duration) * (1f - pressedScale));
            _velocity = 0f;
        }
        else
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            const float k = 520f, c = 34f;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.004f));
            float h = dt / steps;

            for (int i = 0; i < steps; i++)
            {
                float a = -k * (_scale - goal) - c * _velocity;
                _velocity += a * h;
                _scale += _velocity * h;
            }

            if (Mathf.Abs(_scale - goal) < 0.0005f && Mathf.Abs(_velocity) < 0.005f)
            {
                _scale = goal;
                _velocity = 0f;
            }
        }

        if (_rect != null)
            _rect.localScale = new Vector3(_scale, _scale, 1f);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (target != null && !target.IsInteractable())
            return;

        _held = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _held = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _held = false;
    }
}
