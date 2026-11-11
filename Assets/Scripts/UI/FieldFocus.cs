using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows which text field is being typed into.
///
/// A field drawn the same whether it has the keyboard or not left people
/// tapping the box again to see if it had taken, and on a form with three boxes
/// they could not tell which one the letters were going into. Focused, a field
/// gets a 2px blue edge, a faint blue wash and a thick blinking caret in the
/// same blue; unfocused, a 1px hairline.
///
/// The edge is two outline images the builder puts under the field (see
/// AppUIBuilder.AddFieldFocus): the outer one is always on and carries the
/// hairline or the blue, the inner one is inset by a pixel and only on while
/// focused, which together make the 2px.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_InputField))]
public class FieldFocus : MonoBehaviour
{
    [Tooltip("Always on: the hairline, or the outer half of the blue edge.")]
    public Image edge;

    [Tooltip("On only while focused: the inner half of the blue edge.")]
    public Image edgeInner;

    [Tooltip("The field's own background. Washed with the blue while focused.")]
    public Graphic fill;

    [Tooltip("The role the background is themed with. Its resting colour.")]
    public ThemeRole fillRole = ThemeRole.Surface;

    [Tooltip("On focus, scrolls the list this field is in so the field sits in " +
             "the top part of it, above where the keyboard comes up.")]
    public bool scrollIntoView = true;

    // The focus blue: Daylight's focus ring, and the dark theme's link blue.
    static readonly Color FocusLight = new Color32(0x00, 0x71, 0xE3, 0xFF);
    static readonly Color FocusDark = new Color32(0x29, 0x97, 0xFF, 0xFF);

    // The resting hairline, as the A5 sign-up draws its fields.
    static readonly Color HairLight = new Color32(0xD2, 0xD2, 0xD7, 0xFF);
    static readonly Color HairDark = new Color32(0x33, 0x36, 0x3B, 0xFF);

    const float WashLight = 0.06f;
    const float WashDark = 0.12f;

    public const int CaretWidth = 3;
    public const float CaretBlinkRate = 0.85f;

    TMP_InputField _field;
    ScrollRect _scroll;
    bool _focused;
    bool _dirty = true;

    /// <summary>The blue of the edge and the caret, in the current theme.</summary>
    public static Color Accent => ThemeManager.IsDark ? FocusDark : FocusLight;

    /// <summary>The resting hairline, in the current theme.</summary>
    public static Color Hairline => ThemeManager.IsDark ? HairDark : HairLight;

    void Awake()
    {
        _field = GetComponent<TMP_InputField>();
    }

    void OnEnable()
    {
        if (_field == null)
            _field = GetComponent<TMP_InputField>();

        _scroll = GetComponentInParent<ScrollRect>();

        ThemeManager.OnThemeChanged += MarkDirty;
        _dirty = true;
        Paint(_field != null && _field.isFocused);
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= MarkDirty;

        // Switched off mid-edit (a door closing over it) is not focused when it
        // comes back.
        _focused = false;
    }

    void MarkDirty()
    {
        // Painted in LateUpdate rather than here: ThemedElement repaints the
        // background on the same event, and whichever ran second would win.
        _dirty = true;
    }

    void LateUpdate()
    {
        bool focused = _field != null && _field.isFocused;

        if (focused != _focused || _dirty)
        {
            bool gained = focused && !_focused;
            Paint(focused);

            if (gained && scrollIntoView && TouchScreenKeyboard.isSupported)
                Reveal();
        }
    }

    /// <summary>
    /// Scrolls the field up into the top third of its list if it is in the
    /// lower half, where the keyboard is about to be. Only for a list built the
    /// usual way (content pinned to the top of the viewport); anything else is
    /// left alone.
    /// </summary>
    void Reveal()
    {
        if (_scroll == null || _scroll.content == null)
            return;

        RectTransform content = _scroll.content;
        RectTransform viewport = _scroll.viewport != null ? _scroll.viewport : (RectTransform)_scroll.transform;

        if (content.anchorMax.y < 0.999f || content.pivot.y < 0.999f)
            return;

        RectTransform self = (RectTransform)transform;
        Rect r = self.rect;

        // The field's top and bottom, measured down from the top of the content.
        float top = -content.InverseTransformPoint(self.TransformPoint(new Vector3(0f, r.yMax, 0f))).y;
        float bottom = -content.InverseTransformPoint(self.TransformPoint(new Vector3(0f, r.yMin, 0f))).y;

        float view = viewport.rect.height;
        float scrolled = content.anchoredPosition.y;

        if (top - scrolled >= 0f && bottom - scrolled <= view * 0.45f)
            return;

        float most = Mathf.Max(0f, content.rect.height - view);
        float wanted = Mathf.Clamp(top - view * 0.18f, 0f, most);

        _scroll.StopMovement();
        content.anchoredPosition = new Vector2(content.anchoredPosition.x, wanted);
    }

    /// <summary>
    /// Draws the field as focused or not. Public so the preview can photograph
    /// the focused state without a keyboard.
    /// </summary>
    public void Paint(bool focused)
    {
        _focused = focused;
        _dirty = false;

        Color accent = Accent;

        if (edge != null)
            edge.color = focused ? accent : Hairline;

        if (edgeInner != null)
        {
            edgeInner.color = accent;
            edgeInner.enabled = focused;
        }

        if (fill != null)
        {
            Color rest = ThemeManager.Get(fillRole);
            Color wash = focused
                ? Color.Lerp(rest, accent, ThemeManager.IsDark ? WashDark : WashLight)
                : rest;
            wash.a = rest.a;
            fill.color = wash;
        }

        if (_field == null)
            _field = GetComponent<TMP_InputField>();

        if (_field != null)
        {
            _field.customCaretColor = true;
            _field.caretColor = accent;
            _field.caretWidth = CaretWidth;
            _field.caretBlinkRate = CaretBlinkRate;
        }
    }
}
