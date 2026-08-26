using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lays children out left to right and wraps onto a new line when the row is full.
/// Unity ships no wrapping layout - HorizontalLayoutGroup overflows and
/// GridLayoutGroup forces every cell to one width, which looks wrong for pills
/// whose labels vary from "Modern" to "Предсоциалистически".
/// </summary>
[AddComponentMenu("Layout/Flow Layout Group")]
public class FlowLayoutGroup : LayoutGroup
{
    public float spacingX = 8f;
    public float spacingY = 8f;

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();
        SetLayoutInputForAxis(0f, 0f, -1f, 0);
    }

    public override void CalculateLayoutInputVertical()
    {
        float height = LayoutRows(rectTransform.rect.width, false);
        SetLayoutInputForAxis(height, height, -1f, 1);
    }

    public override void SetLayoutHorizontal()
    {
        LayoutRows(rectTransform.rect.width, true);
    }

    public override void SetLayoutVertical()
    {
        LayoutRows(rectTransform.rect.width, true);
    }

    float LayoutRows(float containerWidth, bool apply)
    {
        if (rectChildren == null)
            return padding.vertical;

        float available = containerWidth - padding.horizontal;
        float x = padding.left;
        float y = padding.top;
        float rowHeight = 0f;

        for (int i = 0; i < rectChildren.Count; i++)
        {
            RectTransform child = rectChildren[i];

            // Unity tears the scene down while layouts are still rebuilding, so by
            // the time this runs a child may already be destroyed. Touching one
            // throws inside a native callback, which takes the whole editor down
            // rather than surfacing as an exception.
            if (child == null)
                continue;

            float width = LayoutUtility.GetPreferredSize(child, 0);
            float height = LayoutUtility.GetPreferredSize(child, 1);

            width = Mathf.Min(width, available);

            bool startsRow = x > padding.left;
            if (startsRow && x + width > padding.left + available)
            {
                x = padding.left;
                y += rowHeight + spacingY;
                rowHeight = 0f;
            }

            if (apply)
            {
                SetChildAlongAxis(child, 0, x, width);
                SetChildAlongAxis(child, 1, y, height);
            }

            x += width + spacingX;
            rowHeight = Mathf.Max(rowHeight, height);
        }

        return y + rowHeight + padding.bottom;
    }
}
