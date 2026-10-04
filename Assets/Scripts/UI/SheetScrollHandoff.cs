using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Lets a sheet's scrolling body hand a drag to the sheet: pulled up while the
/// sheet is half open, the sheet grows; pulled down with the text at its top, the
/// sheet shrinks or goes. Otherwise the text scrolls, as it would anywhere.
///
/// Works for both kinds of sheet: the stop sheet with its two heights
/// (SheetDetents), and the pages and cards that only swipe away
/// (SheetDragHandle) - for those it finds the handle on whatever sheet it is
/// in, so every list gets this without being told which sheet it belongs to.
///
/// On the same object as the ScrollRect. Both hear every drag; when this one
/// takes it, it stops the ScrollRect moving for the rest of that drag.
/// </summary>
public class SheetScrollHandoff : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public ScrollRect scroll;
    public SheetDetents detents;
    public SheetDragHandle handle;

    bool _mine;
    bool _looked;

    /// <summary>
    /// The sheet this list is in: the nearest one that swipes away, or the stop
    /// sheet with its two heights. Lists that are in no sheet - Profile, the
    /// settings pages - find neither and scroll as they always did.
    /// </summary>
    void FindSheet()
    {
        _looked = true;

        for (Transform t = transform.parent; t != null && handle == null && detents == null; t = t.parent)
        {
            handle = t.GetComponent<SheetDragHandle>();

            // The stop sheet's heights live on its header, beside the body.
            if (handle == null && t.GetComponent<SheetAnimator>() != null)
            {
                detents = t.GetComponentInChildren<SheetDetents>();
                break;
            }
        }
    }

    void Awake()
    {
        if (scroll == null)
            scroll = GetComponent<ScrollRect>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _mine = false;

        if (scroll == null)
            return;

        if (!_looked)
            FindSheet();

        if (detents == null && handle == null)
            return;

        // The whole way from where the finger went down, not this frame's step:
        // the first step of a drag is a few pixels and says little about which
        // way it is going.
        Vector2 d = eventData.position - eventData.pressPosition;
        if (Mathf.Abs(d.x) > Mathf.Abs(d.y))
            return;

        bool atTop = scroll.verticalNormalizedPosition >= 0.999f ||
                     scroll.content == null || scroll.viewport == null ||
                     scroll.content.rect.height <= scroll.viewport.rect.height + 1f;

        _mine = detents != null
            ? (d.y > 0f && !detents.IsLarge) || (d.y < 0f && atTop)
            : d.y < 0f && atTop;

        if (!_mine)
            return;

        scroll.vertical = false;
        scroll.velocity = Vector2.zero;

        if (detents != null)
            detents.OnBeginDrag(eventData);
        else
            handle.OnBeginDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_mine)
            return;

        if (detents != null)
            detents.OnDrag(eventData);
        else
            handle.OnDrag(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (!_mine)
            return;

        _mine = false;
        scroll.vertical = true;

        if (detents != null)
            detents.OnEndDrag(eventData);
        else
            handle.OnEndDrag(eventData);
    }

    void OnDisable()
    {
        // Closed in the middle of a pull: the list scrolls again next time.
        if (_mine && scroll != null)
            scroll.vertical = true;

        _mine = false;
    }
}
