using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// An Image that swaps its picture with the theme rather than its colour: the
/// meARc lockup is drawn in ink on light and in white on dark, and tinting one
/// artwork cannot do that without also tinting the blue.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Image))]
public class ThemedSprite : MonoBehaviour
{
    public Sprite onLight;
    public Sprite onDark;

    void OnEnable()
    {
        Apply();
        ThemeManager.OnThemeChanged += Apply;
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Apply;
    }

    public void Apply()
    {
        Image image = GetComponent<Image>();
        Sprite wanted = ThemeManager.IsDark && onDark != null ? onDark : onLight;

        if (image != null && wanted != null)
            image.sprite = wanted;
    }
}
