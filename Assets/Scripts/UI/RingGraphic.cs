using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A circle outline drawn as geometry, at a thickness that does not change with
/// its radius.
///
/// The accuracy circle used to be a 64-pixel ring texture scaled up to whatever
/// the GPS said. Stretching a 7-pixel stroke across 600 pixels of screen gives a
/// 65-pixel blur, which is exactly what it looked like: a soft glowing smudge over
/// the map rather than a measurement. A mesh has no resolution to run out of.
///
/// The edge is feathered across one pixel by hand, because UI geometry gets no
/// antialiasing of its own and a hard-edged circle crawls with jagged steps as it
/// grows.
///
/// The RequireComponent is not decoration. Graphic declares one for CanvasRenderer,
/// but a subclass does not reliably inherit it through AddComponent, and without a
/// CanvasRenderer this throws on every frame it tries to draw.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class RingGraphic : MaskableGraphic
{
    [Tooltip("Stroke width in canvas units. Constant, whatever the radius.")]
    public float thickness = 3f;

    [Tooltip("More segments is smoother and costs more triangles. Ninety-six is " +
             "smooth at any size a phone can show.")]
    [Range(12, 180)]
    public int segments = 96;

    [Tooltip("Width of the fade at each edge. One unit is enough to stop the " +
             "circle from crawling as it changes size.")]
    public float feather = 1.25f;

    public void SetThickness(float value)
    {
        if (Mathf.Approximately(thickness, value))
            return;

        thickness = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        float outer = Mathf.Min(r.width, r.height) * 0.5f;

        if (outer <= 0f)
            return;

        float half = Mathf.Min(thickness, outer * 2f) * 0.5f;
        float mid = Mathf.Max(half, outer - half);

        // Four rings of vertices: transparent, solid, solid, transparent. The two
        // solid ones are the stroke; the outer pair fade it out.
        float[] radii =
        {
            Mathf.Max(0f, mid - half - feather),
            Mathf.Max(0f, mid - half),
            mid + half,
            mid + half + feather
        };

        Color32 solid = color;
        Color32 clear = new Color(color.r, color.g, color.b, 0f);
        Color32[] colours = { clear, solid, solid, clear };

        for (int i = 0; i <= segments; i++)
        {
            float angle = (float)i / segments * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);

            for (int band = 0; band < 4; band++)
            {
                vh.AddVert(new Vector3(cos * radii[band], sin * radii[band], 0f),
                           colours[band], Vector2.zero);
            }
        }

        for (int i = 0; i < segments; i++)
        {
            int a = i * 4;
            int b = (i + 1) * 4;

            for (int band = 0; band < 3; band++)
            {
                vh.AddTriangle(a + band, a + band + 1, b + band + 1);
                vh.AddTriangle(a + band, b + band + 1, b + band);
            }
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        base.OnValidate();
        SetVerticesDirty();
    }
#endif
}
