using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Hands a drag on a page with nothing scrollable on it - a photo, a video - to
/// the stop sheet it is in, so it can be pulled down or up like the rest of the
/// sheet. Anything on the page that drags for itself (a scroll list, a slider)
/// is nearer the finger and keeps its own drags.
/// </summary>
public class DetentsDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public SheetDetents detents;

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (detents != null)
            detents.OnBeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (detents != null)
            detents.OnDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (detents != null)
            detents.OnEndDrag(eventData);
    }
}
