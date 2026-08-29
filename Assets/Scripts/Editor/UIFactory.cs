using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum UIShape
{
    Rect,
    Rounded,
    Pill,
    Circle,

    /// <summary>A circle with a hole, for the ring that separates a marker dot.</summary>
    Ring,

    /// <summary>Points up at zero rotation. Every direction arrow in the app.</summary>
    Triangle
}

/// <summary>
/// Construction helpers for the generated UI. Two rules are enforced here rather
/// than remembered at every call site: anything carrying colour gets a
/// <see cref="ThemedElement"/> so the light/dark switch is complete, and anything
/// with a background gets a shape, because Unity's default sprite is a hard
/// rectangle.
/// </summary>
public static class UIFactory
{
    public static RectTransform Rect(string name, Transform parent)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static void BottomStretch(RectTransform rt, float height, float y = 0f)
    {
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = new Vector2(0f, y);
    }

    public static void TopStretch(RectTransform rt, float height, float y = 0f)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(0f, height);
        rt.anchoredPosition = new Vector2(0f, -y);
    }

    public static void Corner(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 offset)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = offset;
    }

    // ------------------------------------------------------------------ shapes

    public static void ApplyShape(Image image, UIShape shape)
    {
        switch (shape)
        {
            case UIShape.Rounded:
                image.sprite = UISprites.Rounded;
                image.type = Image.Type.Sliced;
                break;

            case UIShape.Pill:
                image.sprite = UISprites.Pill;
                image.type = Image.Type.Sliced;
                break;

            case UIShape.Circle:
                image.sprite = UISprites.Circle;
                image.type = Image.Type.Simple;
                break;

            case UIShape.Ring:
                image.sprite = UISprites.Ring;
                image.type = Image.Type.Simple;
                break;

            case UIShape.Triangle:
                image.sprite = UISprites.Triangle;
                image.type = Image.Type.Simple;
                break;
        }

        // Keeps the authored corner radius instead of letting Unity rescale the
        // 9-slice border with the sprite's pixels-per-unit.
        if (shape != UIShape.Rect)
            image.pixelsPerUnitMultiplier = 1f;
    }

    /// <summary>Background that follows the theme.</summary>
    public static Image Fill(RectTransform rt, ThemeRole role, float alpha = 1f,
                             UIShape shape = UIShape.Rect)
    {
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = ThemeManager.Get(role);
        ApplyShape(image, shape);

        if (alpha < 1f)
        {
            Color c = image.color;
            c.a = alpha;
            image.color = c;
        }

        ThemedElement themed = rt.gameObject.AddComponent<ThemedElement>();
        themed.role = role;
        themed.preserveAlpha = alpha < 1f;

        return image;
    }

    /// <summary>
    /// Background with no ThemedElement, for anything whose colour is driven by its
    /// own component - a filter pill, a switch track - where both would fight.
    /// </summary>
    public static Image Plain(RectTransform rt, Color colour, UIShape shape = UIShape.Rect)
    {
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = colour;
        ApplyShape(image, shape);
        return image;
    }

    // -------------------------------------------------------------------- text

    public static TextMeshProUGUI Text(string name, Transform parent, float size,
                                       ThemeRole role, TextAlignmentOptions align,
                                       string localizationKey = null,
                                       FontStyles style = FontStyles.Normal)
    {
        RectTransform rt = Rect(name, parent);

        TextMeshProUGUI text = rt.gameObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = align;
        text.fontStyle = style;
        text.color = ThemeManager.Get(role);
        text.raycastTarget = false;
        text.text = name;

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        ThemedElement themed = rt.gameObject.AddComponent<ThemedElement>();
        themed.role = role;

        if (!string.IsNullOrEmpty(localizationKey))
        {
            LocalizedLabel label = rt.gameObject.AddComponent<LocalizedLabel>();
            label.key = localizationKey;
            text.text = AppStrings.Get(localizationKey);
        }

        return text;
    }

    // ----------------------------------------------------------------- controls

    public static Button Button(RectTransform rt, ThemeRole background, float alpha = 1f,
                                UIShape shape = UIShape.Rect)
    {
        Image image = Fill(rt, background, alpha, shape);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = image;

        Feedback(button);
        return button;
    }

    /// <summary>
    /// Replaces Unity's colour tint with a dim on the whole control.
    ///
    /// The tint acts on the target graphic, and most controls here have an
    /// invisible one so a whole row can be tapped - a nav tab, a list row, a
    /// settings row. Tinting an alpha-zero rectangle is no feedback at all, which
    /// left most of the app answering taps with nothing.
    /// </summary>
    public static TapFeedback Feedback(Button button)
    {
        button.transition = Selectable.Transition.None;

        if (button.GetComponent<CanvasGroup>() == null)
            button.gameObject.AddComponent<CanvasGroup>();

        TapFeedback feedback = button.GetComponent<TapFeedback>();
        if (feedback == null)
            feedback = button.gameObject.AddComponent<TapFeedback>();

        feedback.target = button;
        return feedback;
    }

    /// <summary>
    /// A Toggle drawn as a sliding switch. Unity's own Toggle can only show and
    /// hide a graphic, which reads as a box with a square in it.
    /// </summary>
    public static Toggle Switch(string name, Transform parent)
    {
        RectTransform rt = Rect(name, parent);
        rt.sizeDelta = new Vector2(96f, 54f);

        LayoutElement element = Size(rt.gameObject, 54f, 96f);
        element.flexibleWidth = 0f; // never stretch to fill its row

        Image track = Plain(rt, ThemeManager.Get(ThemeRole.Line), UIShape.Pill);

        RectTransform knob = Rect("Knob", rt);
        knob.sizeDelta = new Vector2(40f, 40f);
        knob.anchorMin = new Vector2(0f, 0.5f);
        knob.anchorMax = new Vector2(0f, 0.5f);
        knob.pivot = new Vector2(0f, 0.5f);
        knob.anchoredPosition = new Vector2(7f, 0f);

        Image knobImage = Plain(knob, Color.white, UIShape.Circle);
        knobImage.raycastTarget = false;

        Toggle toggle = rt.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = track;
        toggle.graphic = null;
        toggle.transition = Selectable.Transition.None;

        SwitchView view = rt.gameObject.AddComponent<SwitchView>();
        view.toggle = toggle;
        view.track = track;
        view.knob = knob;
        view.knobImage = knobImage;

        return toggle;
    }

    public static TMP_InputField InputField(string name, Transform parent,
                                            string placeholderKey, out TextMeshProUGUI placeholder)
    {
        RectTransform rt = Rect(name, parent);
        Size(rt.gameObject, 96f);
        Fill(rt, ThemeRole.Surface, 1f, UIShape.Rounded);

        RectTransform area = Rect("Text Area", rt);
        Stretch(area);
        area.offsetMin = new Vector2(30f, 8f);
        area.offsetMax = new Vector2(-30f, -8f);
        area.gameObject.AddComponent<RectMask2D>();

        placeholder = Text("Placeholder", area, 38f, ThemeRole.TextMuted,
                           TextAlignmentOptions.Left, placeholderKey);
        Stretch(placeholder.rectTransform);

        TextMeshProUGUI textComponent = Text("Text", area, 38f, ThemeRole.TextPrimary,
                                             TextAlignmentOptions.Left);
        Stretch(textComponent.rectTransform);
        textComponent.text = string.Empty;

        TMP_InputField field = rt.gameObject.AddComponent<TMP_InputField>();
        field.textViewport = area;
        field.textComponent = textComponent;
        field.placeholder = placeholder;
        field.targetGraphic = rt.GetComponent<Image>();
        field.transition = Selectable.Transition.None;

        return field;
    }

    /// <summary>
    /// Draws a close cross from two rotated bars rather than a text glyph. The
    /// project's font atlas is Latin-only, so multiplication signs and similar
    /// symbols render as empty boxes on device.
    /// </summary>
    public static void Cross(RectTransform parent, ThemeRole role,
                             float size = 26f, float thickness = 3f)
    {
        for (int i = 0; i < 2; i++)
        {
            RectTransform bar = Rect("Bar" + i, parent);
            Corner(bar, new Vector2(0.5f, 0.5f), new Vector2(size, thickness), Vector2.zero);

            Image image = Fill(bar, role);
            image.raycastTarget = false;

            bar.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
        }
    }

    // ------------------------------------------------------------------ layout

    public static VerticalLayoutGroup Card(RectTransform rt, float padding = 24f,
                                           float spacing = 16f, bool fit = false)
    {
        Fill(rt, ThemeRole.Surface, 1f, UIShape.Rounded);

        VerticalLayoutGroup layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset((int)padding, (int)padding, (int)padding, (int)padding);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        // A VerticalLayoutGroup already reports its own preferred height, so a
        // fitter is only needed when this card has no layout group above it.
        if (fit)
        {
            ContentSizeFitter fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        return layout;
    }

    public static VerticalLayoutGroup VerticalStack(RectTransform rt, float spacing,
                                                    bool fitHeight = true)
    {
        VerticalLayoutGroup layout = rt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        if (fitHeight)
        {
            ContentSizeFitter fitter = rt.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        return layout;
    }

    public static HorizontalLayoutGroup HorizontalStack(RectTransform rt, float spacing,
                                                        bool expandWidth = false)
    {
        HorizontalLayoutGroup layout = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = expandWidth;
        layout.childForceExpandHeight = false;
        layout.childAlignment = TextAnchor.MiddleLeft;
        return layout;
    }

    public static LayoutElement Size(GameObject go, float height, float width = -1f)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();
        if (element == null)
            element = go.AddComponent<LayoutElement>();

        element.minHeight = height;
        element.preferredHeight = height;

        if (width > 0f)
        {
            element.minWidth = width;
            element.preferredWidth = width;
        }

        return element;
    }

    /// <summary>Lets one child of a row absorb the leftover width.</summary>
    public static LayoutElement Flexible(GameObject go, float weight = 1f)
    {
        LayoutElement element = go.GetComponent<LayoutElement>();
        if (element == null)
            element = go.AddComponent<LayoutElement>();

        element.flexibleWidth = weight;
        return element;
    }

    public static RectTransform ScrollList(string name, Transform parent, float spacing = 0f)
    {
        RectTransform root = Rect(name, parent);
        Stretch(root);

        ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 30f;

        RectTransform viewport = Rect("Viewport", root);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = Rect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.sizeDelta = Vector2.zero;
        content.anchoredPosition = Vector2.zero;

        VerticalStack(content, spacing);

        scroll.viewport = viewport;
        scroll.content = content;

        return content;
    }
}
