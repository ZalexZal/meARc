using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

/// <summary>
/// Makes the grab bar at the top of a bottom sheet actually work: drag the sheet
/// down past a threshold to dismiss it, or let go short of that and it settles
/// back. The handle was drawn before this existed, which promised a gesture the
/// sheet didn't have.
///
/// Only the strip around the grab bar is draggable, so a scrolling list inside the
/// sheet keeps its own vertical drags.
/// </summary>
public class SheetDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("References")]
    public SheetAnimator sheet;
    public RectTransform sheetRect;

    [Header("Behaviour")]
    [Tooltip("How far down the sheet must be dragged before letting go dismisses it.")]
    public float dismissDistance = 150f;

    [Tooltip("Invoked when the drag passes the threshold. Wire this to the sheet's " +
             "own close method so scrims and state are cleaned up too.")]
    // Initialised here on purpose. Unity only creates a UnityEvent instance when it
    // deserializes the component; straight after AddComponent the field is still
    // null, and anything trying to add a listener to it throws.
    public UnityEvent onDismissed = new UnityEvent();

    float _startY;
    float _scale = 1f;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (sheetRect == null)
            return;

        _startY = sheetRect.anchoredPosition.y;

        // Pointer deltas arrive in screen pixels; the canvas works in reference
        // pixels, so without this the sheet moves at the wrong speed on every
        // device except one.
        Canvas canvas = GetComponentInParent<Canvas>();
        _scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (sheetRect == null)
            return;

        float delta = eventData.delta.y / _scale;
        float target = sheetRect.anchoredPosition.y + delta;

        // Downward only - dragging a bottom sheet upward past its resting place
        // just detaches it from the bottom of the screen.
        target = Mathf.Min(target, _startY);

        sheetRect.anchoredPosition = new Vector2(sheetRect.anchoredPosition.x, target);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (sheetRect == null)
            return;

        float dragged = _startY - sheetRect.anchoredPosition.y;

        if (dragged >= dismissDistance)
        {
            if (onDismissed != null)
                onDismissed.Invoke();

            return;
        }

        if (sheet != null)
            sheet.SettleOpen();
        else
            sheetRect.anchoredPosition = new Vector2(sheetRect.anchoredPosition.x, _startY);
    }
}
