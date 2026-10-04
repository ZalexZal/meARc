using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Lets a sheet be swiped away: drag it down far enough, or flick it down, and
/// it slides off and closes; let go short of that and it settles back.
///
/// Put on the sheet itself, so a drag that starts anywhere on it - the header,
/// the words, a button - is the sheet's. A list inside it keeps its own drags
/// and hands this the ones that pull down from its top (SheetScrollHandoff).
/// </summary>
public class SheetDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("References")]
    [Tooltip("A sheet that animates itself away. Without one, this slides the " +
             "sheet off before closing it.")]
    public SheetAnimator sheet;
    public RectTransform sheetRect;

    [Header("Behaviour")]
    [Tooltip("How far down the sheet must be dragged before letting go dismisses it.")]
    public float dismissDistance = 150f;

    [Tooltip("A flick down at least this fast closes it however short it was, in " +
             "reference pixels a second.")]
    public float flickSpeed = 1400f;

    [Tooltip("Invoked when the drag passes the threshold. Wire this to the sheet's " +
             "own close method so scrims and state are cleaned up too.")]
    // Initialised here on purpose. Unity only creates a UnityEvent instance when it
    // deserializes the component; straight after AddComponent the field is still
    // null, and anything trying to add a listener to it throws.
    public UnityEvent onDismissed = new UnityEvent();

    [Tooltip("Pressed on dismiss instead of a listener, for a page whose close " +
             "button already knows how to close it.")]
    public UnityEngine.UI.Button pressOnDismiss;

    float _startY;
    float _scale = 1f;
    float _velocity;
    float _lastTime;
    bool _dragging;
    bool _closing;

    void OnDisable()
    {
        // Closed by something else part way through: back where it rests.
        if ((_closing || _dragging) && sheetRect != null)
            sheetRect.anchoredPosition = new Vector2(sheetRect.anchoredPosition.x, _startY);

        _closing = false;
        _dragging = false;
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (sheetRect == null || _closing)
            return;

        StopAllCoroutines();
        _dragging = true;
        _startY = sheetRect.anchoredPosition.y;
        _velocity = 0f;
        _lastTime = Time.unscaledTime;

        // Pointer deltas arrive in screen pixels; the canvas works in reference
        // pixels, so without this the sheet moves at the wrong speed on every
        // device except one.
        Canvas canvas = GetComponentInParent<Canvas>();
        _scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        float delta = eventData.delta.y / _scale;
        float now = Time.unscaledTime;
        _velocity = Mathf.Lerp(_velocity, delta / Mathf.Max(0.001f, now - _lastTime), 0.45f);
        _lastTime = now;

        // Downward only - dragging a bottom sheet upward past its resting place
        // just detaches it from the bottom of the screen.
        float target = Mathf.Min(sheetRect.anchoredPosition.y + delta, _startY);
        sheetRect.anchoredPosition = new Vector2(sheetRect.anchoredPosition.x, target);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_dragging)
            return;

        _dragging = false;

        float dragged = _startY - sheetRect.anchoredPosition.y;
        bool flicked = _velocity < -flickSpeed && dragged > 20f;

        if (dragged < dismissDistance && !flicked)
        {
            if (sheet != null)
                sheet.SettleOpen();
            else
                StartCoroutine(SlideTo(_startY, 0.2f));

            return;
        }

        // An animated sheet takes itself away from where it was let go.
        if (sheet != null)
        {
            Dismiss();
            return;
        }

        StartCoroutine(SlideOff());
    }

    /// <summary>On down and off the screen from wherever it was let go, then closed.</summary>
    IEnumerator SlideOff()
    {
        _closing = true;
        float off = _startY - Mathf.Max(sheetRect.rect.height, 400f) - 60f;

        yield return SlideTo(off, 0.18f);

        _closing = false;
        Dismiss();

        // The page is gone now; put it back where it rests, so it opens there
        // next time rather than where the finger let go.
        sheetRect.anchoredPosition = new Vector2(sheetRect.anchoredPosition.x, _startY);
    }

    void Dismiss()
    {
        if (onDismissed != null)
            onDismissed.Invoke();

        if (pressOnDismiss != null)
            pressOnDismiss.onClick.Invoke();
    }

    IEnumerator SlideTo(float y, float seconds)
    {
        float from = sheetRect.anchoredPosition.y;
        float t = 0f;

        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / seconds);
            float k = 1f - Mathf.Pow(1f - t, 3f);
            sheetRect.anchoredPosition = new Vector2(sheetRect.anchoredPosition.x, Mathf.Lerp(from, y, k));
            yield return null;
        }
    }
}
