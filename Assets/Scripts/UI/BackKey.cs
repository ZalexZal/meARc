using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Marks a control as the way out of whatever it is sitting on.
///
/// Put on the Close, Cancel and Back buttons the screens already have. It adds
/// no behaviour of its own - <see cref="BackKey"/> looks for these and presses
/// the topmost one when the phone's back button is used, so back does the same
/// thing as the control the reader can see rather than a second, parallel idea
/// of what "back" means.
/// </summary>
public class WayOut : MonoBehaviour
{
}

/// <summary>
/// The phone's back button, which did nothing at all.
///
/// Android readers press back to close things. Nothing in this app listened, so
/// every card was escapable only by finding its own Close - and when the Team
/// card lost its Close to a layout fault, the app became unrecoverable: the
/// navigation bar and every map control are switched off behind an open door,
/// and there was no key that could shut it. A soft lock needs two mistakes and
/// this was the second one.
///
/// Rather than keep a second list of what "back" means per screen - which would
/// drift from the buttons the moment either changed - this presses the visible
/// one. The topmost active <see cref="WayOut"/> wins, where topmost means last
/// in the drawing order, which is what "on top" already means in a Canvas.
/// </summary>
[DefaultExecutionOrder(100)]
public class BackKey : MonoBehaviour
{
    void Update()
    {
        // Escape is the back button on Android, and the Escape key elsewhere,
        // which is the same gesture in both places.
        if (!Input.GetKeyDown(KeyCode.Escape))
            return;

        Button top = Topmost();

        if (top != null)
            top.onClick.Invoke();

        // Nothing open: the press is swallowed rather than closing the app.
        // Somebody standing in the street mid-trail who taps back once should
        // not lose the screen; leaving is what the home button is for.
    }

    static Button Topmost()
    {
        WayOut[] outs = FindObjectsByType<WayOut>(FindObjectsInactive.Exclude,
                                                  FindObjectsSortMode.None);

        Button best = null;
        long bestDepth = long.MinValue;

        for (int i = 0; i < outs.Length; i++)
        {
            Button button = outs[i].GetComponent<Button>();

            if (button == null || !button.isActiveAndEnabled || !button.interactable)
                continue;

            // A closed bottom sheet slides off the screen and stays active, so
            // its close button kept winning: Back "closed" a sheet nobody could
            // see while the Progress page in front of the reader stayed put.
            if (!OnScreen(button.transform as RectTransform))
                continue;

            long depth = DrawOrder(outs[i].transform);

            if (depth > bestDepth)
            {
                bestDepth = depth;
                best = button;
            }
        }

        return best;
    }

    /// <summary>
    /// How late in the drawing this sits, as one comparable number.
    ///
    /// A Canvas draws children in order, so a later sibling covers an earlier
    /// one, and that holds at every level. Walking to the root and packing the
    /// sibling indices with the shallowest most significant gives exactly that
    /// ordering: two controls compare first on the branch nearest the root that
    /// separates them, which is the one that decides who is on top.
    /// </summary>
    static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>The overlay canvas draws in screen pixels, so world corners are screen corners.</summary>
    static bool OnScreen(RectTransform rect)
    {
        if (rect == null)
            return true;

        rect.GetWorldCorners(Corners);
        return Corners[2].x > 0f && Corners[0].x < Screen.width &&
               Corners[2].y > 0f && Corners[0].y < Screen.height;
    }

    static long DrawOrder(Transform node)
    {
        long order = 0L;

        // Sixteen levels of six bits. Deeper than that and the comparison is
        // decided long before, which is the right answer anyway.
        Transform[] path = new Transform[16];
        int count = 0;

        for (Transform at = node; at != null && count < path.Length; at = at.parent)
            path[count++] = at;

        for (int i = count - 1; i >= 0; i--)
            order = (order << 6) | (long)Mathf.Clamp(path[i].GetSiblingIndex(), 0, 63);

        return order;
    }
}
