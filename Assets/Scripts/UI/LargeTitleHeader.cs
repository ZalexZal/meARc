using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The large title at the top of Profile, and the slim glass bar that takes its
/// place once it has scrolled away: the title fades as it goes under the status
/// bar, and the bar fades in with the same word, so the reader never loses
/// where they are.
/// </summary>
public class LargeTitleHeader : MonoBehaviour
{
    public ScrollRect scroll;
    public CanvasGroup bigTitle;
    public CanvasGroup bar;

    [Tooltip("How far the page scrolls before the bar shows, in reference pixels.")]
    public float threshold = 111f;

    float _barAlpha;

    void OnEnable()
    {
        _barAlpha = 0f;
        Apply(true);
    }

    void LateUpdate()
    {
        Apply(false);
    }

    void OnDisable()
    {
        // A page pushed over Profile has its own back circle in that corner.
        _barAlpha = 0f;
        if (bar != null)
        {
            bar.alpha = 0f;
            bar.blocksRaycasts = false;
        }
    }

    void Apply(bool snap)
    {
        float y = scroll != null && scroll.content != null ? scroll.content.anchoredPosition.y : 0f;

        if (bigTitle != null)
            bigTitle.alpha = Mathf.Clamp01(1f - (y - 22f) / 83f);

        float goal = y > threshold ? 1f : 0f;
        _barAlpha = snap ? goal : Mathf.MoveTowards(_barAlpha, goal, Time.unscaledDeltaTime / 0.2f);

        if (bar != null)
        {
            bar.alpha = _barAlpha;
            bar.blocksRaycasts = _barAlpha > 0.5f;
        }
    }
}
