using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a pill a pill at whatever height it is drawn.
///
/// The pill sprite's caps are 80px, made for a 160px pill. Unity shrinks a
/// 9-slice border to fit only along the axis that is too short for it, so on a
/// 100px chip the caps came out 80 wide and 50 tall - flattened ovals where the
/// rest of the app has half-circles. Scaling the sprite to the rect's own height
/// makes the caps exactly half of it, on both axes.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Image))]
public class PillShape : MonoBehaviour
{
    [Tooltip("The sprite's cap radius doubled: the height it was drawn for.")]
    public float spriteHeight = 160f;

    Image _image;

    void OnEnable()
    {
        Fit();
    }

    void OnRectTransformDimensionsChange()
    {
        Fit();
    }

    void Fit()
    {
        if (_image == null)
            _image = GetComponent<Image>();

        Rect r = ((RectTransform)transform).rect;
        float shortSide = Mathf.Min(r.width, r.height);

        if (_image != null && shortSide > 1f)
            _image.pixelsPerUnitMultiplier = spriteHeight / shortSide;
    }
}
