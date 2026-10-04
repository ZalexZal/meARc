using UnityEngine;

/// <summary>
/// Keeps a RectTransform clear of the notch, the camera cutout and the gesture bar.
///
/// Applied to content rather than to backgrounds: a nav bar's fill should run to
/// the bottom edge of the glass, and only its buttons should stop short of the
/// gesture bar. Anything else leaves a black strip along the bottom of the phone.
///
/// The insets are recomputed whenever the screen changes, which is what happens on
/// a rotation and on a fold - not only at launch.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SafeAreaPad : MonoBehaviour
{
    [Tooltip("Which edges to keep clear. Most elements need one or two, not all four.")]
    public bool top;
    public bool bottom;
    public bool left;
    public bool right;

    [Tooltip("Added on top of the reported inset, in canvas units. A control flush " +
             "against the edge of the safe area is legal and still uncomfortable.")]
    public float extra;

    RectTransform _rect;
    Canvas _canvas;

    Rect _lastSafeArea;
    Vector2Int _lastResolution;

    void OnEnable()
    {
        _rect = (RectTransform)transform;
        _canvas = GetComponentInParent<Canvas>();

        Apply();
    }

    void Update()
    {
        // Cheap comparison every frame beats missing a rotation. Nothing is written
        // unless something actually changed.
        if (Screen.safeArea == _lastSafeArea &&
            Screen.width == _lastResolution.x &&
            Screen.height == _lastResolution.y)
        {
            return;
        }

        Apply();
    }

    /// <summary>
    /// The top inset in screen pixels: the safe area's, or the camera cutout's
    /// if that is deeper.
    ///
    /// The app hides the status bar, and while it is hidden Android reports a
    /// safe area that runs to the top of the glass - so the trail pill and the
    /// sheets slid up under the punch-hole camera whenever the bar had not
    /// shown since launch. The cutout does not move, so it is the floor.
    /// </summary>
    public static float TopPixels()
    {
        float top = Mathf.Max(0f, Screen.height - Screen.safeArea.yMax);

        Rect[] cutouts = Screen.cutouts;
        if (cutouts != null)
        {
            for (int i = 0; i < cutouts.Length; i++)
            {
                // Screen space counts y up from the bottom; a cutout in the top
                // quarter is one the top of the layout has to clear.
                if (cutouts[i].yMax > Screen.height * 0.75f)
                    top = Mathf.Max(top, Screen.height - cutouts[i].yMin);
            }
        }

        return top;
    }

    void Apply()
    {
        if (_rect == null)
            return;

        _lastSafeArea = Screen.safeArea;
        _lastResolution = new Vector2Int(Screen.width, Screen.height);

        // The canvas works in reference pixels, the screen in real ones, so every
        // inset has to be divided by the scale factor or it is wrong by a third on
        // most phones.
        float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;

        Rect safe = Screen.safeArea;

        float insetLeft = safe.xMin / scale;
        float insetRight = (Screen.width - safe.xMax) / scale;
        float insetBottom = safe.yMin / scale;
        float insetTop = TopPixels() / scale;

        _rect.offsetMin = new Vector2(
            left ? insetLeft + extra : 0f,
            bottom ? insetBottom + extra : 0f);

        _rect.offsetMax = new Vector2(
            right ? -(insetRight + extra) : 0f,
            top ? -(insetTop + extra) : 0f);
    }
}
