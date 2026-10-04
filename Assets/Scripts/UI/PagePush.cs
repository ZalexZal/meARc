using UnityEngine;

/// <summary>
/// A page pushed into a sheet slides in from the right, the way the next screen
/// of anything does, so the reader knows Back will take it away again.
///
/// A third of a second, eased out, and only sideways: the sheet under it stays
/// where it is.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class PagePush : MonoBehaviour
{
    public float duration = 0.32f;

    RectTransform _rect;
    float _t;

    void OnEnable()
    {
        _rect = (RectTransform)transform;
        _t = 0f;
        Place(0f);
    }

    void Update()
    {
        if (_t >= 1f)
            return;

        _t = Mathf.Min(1f, _t + Time.unscaledDeltaTime / Mathf.Max(0.01f, duration));
        Place(_t);
    }

    void Place(float t)
    {
        // Close to the prototype's cubic-bezier(.2, .8, .2, 1): quick, then settling.
        float k = 1f - Mathf.Pow(1f - t, 4f);
        float width = _rect.rect.width > 1f ? _rect.rect.width : 1080f;
        Vector2 p = _rect.anchoredPosition;
        p.x = (1f - k) * width;
        _rect.anchoredPosition = p;
    }
}
