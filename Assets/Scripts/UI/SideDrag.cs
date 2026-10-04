using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// For controls that are dragged sideways inside a page that scrolls up and
/// down - a switch, a segmented control. A drag that is mostly sideways is the
/// control's; one that is mostly up or down is handed to whatever above it
/// scrolls, so the page still moves when a thumb lands on a switch.
/// </summary>
public static class SideDrag
{
    /// <summary>True if the drag is the control's. False once it has been handed on.</summary>
    public static bool Claim(Component control, PointerEventData data)
    {
        Vector2 moved = data.position - data.pressPosition;

        if (Mathf.Abs(moved.x) >= Mathf.Abs(moved.y))
            return true;

        Transform up = control.transform.parent;
        GameObject scroller = up != null ? ExecuteEvents.GetEventHandler<IBeginDragHandler>(up.gameObject) : null;

        // Whatever was pressed is not being tapped any more: a scroll that
        // started on a switch must not flip it when the finger lifts.
        data.eligibleForClick = false;

        if (scroller != null)
        {
            data.pointerDrag = scroller;
            ExecuteEvents.Execute(scroller, data, ExecuteEvents.initializePotentialDrag);
            ExecuteEvents.Execute(scroller, data, ExecuteEvents.beginDragHandler);
        }

        return false;
    }

    /// <summary>How far the finger has moved sideways since it went down, in the control's own units.</summary>
    public static float Moved(Transform control, PointerEventData data)
    {
        float scale = control.lossyScale.x;
        return (data.position.x - data.pressPosition.x) / (Mathf.Abs(scale) > 0.0001f ? scale : 1f);
    }
}
