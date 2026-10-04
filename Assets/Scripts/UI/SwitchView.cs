using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Draws a Toggle as a proper switch: a capsule track that changes colour and a
/// knob that slides between the ends.
///
/// Unity's Toggle only knows how to show and hide a graphic, which reads as a
/// rectangle with a square appearing in it rather than as a switch.
///
/// As on a phone, the knob travels rather than jumps, and it can be pushed
/// across with a finger: let go past the middle and the switch flips.
/// </summary>
public class SwitchView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("References")]
    public Toggle toggle;
    public Image track;
    public RectTransform knob;
    public Image knobImage;

    [Tooltip("Gap between the knob and the end of the track.")]
    public float inset = 7f;

    [Tooltip("Seconds the knob takes to cross, roughly. Daylight's is 0.3.")]
    public float travelTime = 0.09f;

    float _pos;
    float _target;
    float _speed;
    float _dragStart;
    bool _dragging;

    void Awake()
    {
        if (toggle != null)
            toggle.onValueChanged.AddListener(Apply);
    }

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += ApplyCurrent;
        ApplyCurrent();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= ApplyCurrent;
        _dragging = false;
    }

    void OnDestroy()
    {
        if (toggle != null)
            toggle.onValueChanged.RemoveListener(Apply);
    }

    /// <summary>Straight to where the toggle says, without travelling: opening, theme changes.</summary>
    void ApplyCurrent()
    {
        _target = toggle != null && toggle.isOn ? 1f : 0f;
        _pos = _target;
        _speed = 0f;
        Place();
    }

    /// <summary>The toggle changed: travel there.</summary>
    public void Apply(bool isOn)
    {
        _target = isOn ? 1f : 0f;

        if (!isActiveAndEnabled)
        {
            _pos = _target;
            Place();
        }
    }

    void Update()
    {
        if (_dragging)
            return;

        // Set from code without an event still has to show.
        if (toggle != null)
            _target = toggle.isOn ? 1f : 0f;

        if (Mathf.Approximately(_pos, _target))
            return;

        _pos = Mathf.SmoothDamp(_pos, _target, ref _speed, travelTime, Mathf.Infinity, Time.unscaledDeltaTime);

        if (Mathf.Abs(_pos - _target) < 0.002f)
        {
            _pos = _target;
            _speed = 0f;
        }

        Place();
    }

    float Travel
    {
        get
        {
            if (knob == null)
                return 0f;

            float width = ((RectTransform)transform).rect.width;
            return Mathf.Max(0f, width - knob.rect.width - inset * 2f);
        }
    }

    void Place()
    {
        if (track != null)
        {
            // Green when on, as a system switch is; the one blue is kept for
            // the things that are actions.
            track.color = Color.Lerp(ThemeManager.Get(ThemeRole.FillStrong),
                                     ThemeManager.Get(ThemeRole.GoodDot), _pos);
        }

        if (knobImage != null)
            knobImage.color = Color.white;

        if (knob == null)
            return;

        knob.anchorMin = new Vector2(0f, 0.5f);
        knob.anchorMax = new Vector2(0f, 0.5f);
        knob.pivot = new Vector2(0f, 0.5f);
        knob.anchoredPosition = new Vector2(inset + _pos * Travel, 0f);
    }

    // ------------------------------------------------------------- the finger

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (toggle == null || !toggle.IsInteractable() || !SideDrag.Claim(this, eventData))
            return;

        // Toggle sits on this object too, and would flip the switch again on
        // the release that ends this drag.
        eventData.eligibleForClick = false;

        _dragging = true;
        _dragStart = _pos;
        _speed = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        float travel = Travel;
        _pos = Mathf.Clamp01(_dragStart + (travel > 0f ? SideDrag.Moved(transform, eventData) / travel : 0f));
        Place();
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        _dragging = false;
        bool on = _pos >= 0.5f;

        if (toggle.isOn != on)
        {
            Haptics.Tick();
            toggle.isOn = on;
        }

        _target = on ? 1f : 0f;
    }
}
