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
        float insetTop = (Screen.height - safe.yMax) / scale;

        _rect.offsetMin = new Vector2(
            left ? insetLeft + extra : 0f,
            bottom ? insetBottom + extra : 0f);

        _rect.offsetMax = new Vector2(
            right ? -(insetRight + extra) : 0f,
            top ? -(insetTop + extra) : 0f);
    }
}
