using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A row that wraps, as CSS flex-wrap does: children at their preferred size,
/// left to right, onto a new line when the next one would not fit. The stage's
/// sign pills need it - a viewer's three run wider than a phone, and a plain
/// horizontal group squeezed them into each other instead.
/// </summary>
[AddComponentMenu("Layout/Wrap Row")]
public class WrapRow : LayoutGroup
{
    public float spacingX = 16f;
    public float spacingY = 16f;

    readonly List<float> _widths = new List<float>();
    readonly List<float> _heights = new List<float>();

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();

        // Preferred: everything on one line. The parent clamps it to what fits,
        // and the vertical pass wraps against that.
        float total = padding.horizontal;
        for (int i = 0; i < rectChildren.Count; i++)
        {
            float w = LayoutUtility.GetPreferredWidth(rectChildren[i]);
            total += w + (i > 0 ? spacingX : 0f);
        }

        SetLayoutInputForAxis(0f, total, -1f, 0);
    }

    public override void CalculateLayoutInputVertical()
    {
        float height = Arrange(false);
        SetLayoutInputForAxis(height, height, -1f, 1);
    }

    public override void SetLayoutHorizontal()
    {
    }

    public override void SetLayoutVertical()
    {
        Arrange(true);
    }

    float Arrange(bool place)
    {
        float room = rectTransform.rect.width - padding.horizontal;
        float x = 0f;
        float y = padding.top;
        float line = 0f;

        _widths.Clear();
        _heights.Clear();
        for (int i = 0; i < rectChildren.Count; i++)
        {
            _widths.Add(Mathf.Min(LayoutUtility.GetPreferredWidth(rectChildren[i]), room));
            _heights.Add(LayoutUtility.GetPreferredHeight(rectChildren[i]));
        }

        for (int i = 0; i < rectChildren.Count; i++)
        {
            float w = _widths[i];
            float h = _heights[i];

            if (x > 0f && x + w > room + 0.5f)
            {
                y += line + spacingY;
                x = 0f;
                line = 0f;
            }

            if (place)
            {
                SetChildAlongAxis(rectChildren[i], 0, padding.left + x, w);
                SetChildAlongAxis(rectChildren[i], 1, y, h);
            }

            x += w + spacingX;
            line = Mathf.Max(line, h);
        }

        return y + line + padding.bottom;
    }
}
