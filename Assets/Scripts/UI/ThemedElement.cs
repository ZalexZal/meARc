using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tags one Image or text object with the role it plays, so the theme switch can
/// recolour it. Attach it, pick a role in the dropdown, done - no per-screen
/// colour code anywhere.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class ThemedElement : MonoBehaviour
{
    public ThemeRole role = ThemeRole.Surface;

    [Tooltip("Keeps the alpha already set on the graphic instead of taking it from " +
             "the palette. Use for scrims and translucent overlays.")]
    public bool preserveAlpha;

    Graphic _graphic;
    TMP_Text _text;

    void Awake()
    {
        Cache();
    }

    void OnEnable()
    {
        Cache();
        Apply();
        ThemeManager.OnThemeChanged += Apply;
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= Apply;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        Cache();
        Apply();
    }
#endif

    void Cache()
    {
        if (_text == null)
            _text = GetComponent<TMP_Text>();

        if (_graphic == null && _text == null)
            _graphic = GetComponent<Graphic>();
    }

    public void Apply()
    {
        Color colour = ThemeManager.Get(role);

        if (_text != null)
        {
            if (preserveAlpha)
                colour.a = _text.color.a;

            _text.color = colour;
            return;
        }

        if (_graphic != null)
        {
            if (preserveAlpha)
                colour.a = _graphic.color.a;

            _graphic.color = colour;
        }
    }
}
