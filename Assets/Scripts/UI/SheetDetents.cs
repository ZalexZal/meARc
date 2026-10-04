using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The two heights a Daylight sheet rests at, and the drag between them.
///
///   medium   its top at 38% of the screen: the stop and its first lines, with
///            the map still showing above it
///   large    its top just under the status bar: everything, for reading
///
/// The sheet is anchored to the bottom of the screen and this changes its height,
/// so the footer stays where the thumb is at either size. SheetAnimator still
/// slides the whole sheet in and out; this only decides how tall it is.
///
/// Put on the sheet's header, which is what is dragged. A tap on the header that
/// does not move toggles between the two; a drag lets go towards whichever of
/// medium, large or closed it was heading for, judged by where it would have got
/// to at the speed it was going.
/// </summary>
public class SheetDetents : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler,
                            IEndDragHandler
{
    public RectTransform sheetRect;

    [Tooltip("Space kept above the large sheet, under the status bar, in reference pixels.")]
    public float largeGap = 122f;

    [Tooltip("Where the medium sheet's top sits, as a fraction of the screen from the top.")]
    [Range(0.2f, 0.7f)]
    public float mediumTop = 0.38f;

    public UnityEvent onDismissed = new UnityEvent();

    float _height;
    float _velocity;
    float _target;
    bool _springing;
    bool _dragging;
    bool _moved;
    float _lastTime;

    public bool IsLarge { get; private set; }

    Canvas _canvas;

    float CanvasHeight
    {
        get
        {
            if (_canvas == null)
                _canvas = GetComponentInParent<Canvas>();

            Canvas root = _canvas != null ? _canvas.rootCanvas : null;
            return root != null ? ((RectTransform)root.transform).rect.height : 1920f;
        }
    }

    float Scale
    {
        get
        {
            if (_canvas == null)
                _canvas = GetComponentInParent<Canvas>();

            return _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
        }
    }

    /// <summary>The status bar, in reference pixels.</summary>
    float SafeTop
    {
        get
        {
            return SafeAreaPad.TopPixels() / Scale;
        }
    }

    public float LargeHeight { get { return CanvasHeight - largeGap - SafeTop; } }
    public float MediumHeight { get { return CanvasHeight * (1f - mediumTop); } }

    /// <summary>Puts the sheet at a size at once, for opening.</summary>
    public void Snap(bool large)
    {
        IsLarge = large;
        _springing = false;
        _velocity = 0f;
        SetHeight(large ? LargeHeight : MediumHeight);
    }

    /// <summary>Grows to full height, as a page that needs the room is pushed.</summary>
    public void ToLarge()
    {
        if (!IsLarge)
            SpringTo(true, 0f);
    }

    void SpringTo(bool large, float velocity)
    {
        IsLarge = large;
        _target = large ? LargeHeight : MediumHeight;
        _velocity = velocity;
        _springing = true;
    }

    void SetHeight(float h)
    {
        _height = h;

        if (sheetRect != null)
            sheetRect.sizeDelta = new Vector2(sheetRect.sizeDelta.x, h);
    }

    void Update()
    {
        if (!_springing || _dragging)
            return;

        // The same critically damped spring the sheet slides in on: it arrives
        // and stops.
        const float k = 420f, c = 41f;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / 0.004f));
        float h = dt / steps;
        float y = _height;

        for (int i = 0; i < steps; i++)
        {
            float a = -k * (y - _target) - c * _velocity;
            _velocity += a * h;
            y += _velocity * h;
        }

        if (Mathf.Abs(y - _target) < 0.5f && Mathf.Abs(_velocity) < 5f)
        {
            _springing = false;
            y = _target;
            _velocity = 0f;
        }

        SetHeight(y);
    }

    // ------------------------------------------------------------- the header

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_moved)
            return;

        SpringTo(!IsLarge, 0f);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _dragging = true;
        _moved = true;
        _springing = false;
        _velocity = 0f;
        _lastTime = Time.unscaledTime;
        _height = sheetRect != null ? sheetRect.sizeDelta.y : _height;
    }

    public void OnDrag(PointerEventData eventData)
    {
        float delta = eventData.delta.y / Scale;
        float now = Time.unscaledTime;
        float dt = Mathf.Max(0.001f, now - _lastTime);
        _lastTime = now;

        _velocity = Mathf.Lerp(_velocity, delta / dt, 0.45f);

        float h = _height + delta;
        float top = LargeHeight;

        // Past full height it resists, so the sheet never comes away from the
        // bottom of the screen.
        if (h > top)
            h = top + (h - top) * 0.25f;

        SetHeight(Mathf.Max(0f, h));
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _dragging = false;

        // Where the flick was going, a fifth of a second on.
        float projected = _height + _velocity * 0.2f;
        float large = LargeHeight, medium = MediumHeight;

        float toLarge = Mathf.Abs(projected - large);
        float toMedium = Mathf.Abs(projected - medium);
        float toGone = Mathf.Abs(projected);

        if (toGone < toLarge && toGone < toMedium)
        {
            // Keep the height it was let go at. The sheet slides off from there;
            // springing back to half height at the same time put it back on the
            // screen, closed but still showing.
            _springing = false;
            _velocity = 0f;
            if (onDismissed != null)
                onDismissed.Invoke();
            return;
        }

        SpringTo(toLarge < toMedium, _velocity);
    }

    void LateUpdate()
    {
        // A drag ends on this frame or a later one; a click after it is not a tap.
        if (!_dragging && _moved && !Input.GetMouseButton(0) && Input.touchCount == 0)
            _moved = false;
    }
}
