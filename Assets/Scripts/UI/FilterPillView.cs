using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One era or type filter chip. Instantiated per asset, never hand-placed, so the
/// set of pills always matches the set of assets in the project.
///
/// It measures its own width when bound rather than carrying a ContentSizeFitter
/// and a layout group. Nested auto-sizing inside the parent FlowLayoutGroup is the
/// combination that crashes the editor on teardown, and one label measurement is
/// cheaper than three layout passes anyway.
/// </summary>
public class FilterPillView : MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI label;
    public Image background;
    public Button button;
    public LayoutElement layoutElement;

    [Header("Sizing")]
    public float horizontalPadding = 26f;
    public float height = 62f;
    public float minWidth = 90f;

    Action _onClick;
    bool _selected;

    void Awake()
    {
        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += ApplyColours;
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= ApplyColours;
    }

    void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }

    public void Bind(string text, bool selected, Action onClick)
    {
        _onClick = onClick;
        _selected = selected;

        if (label != null)
            label.text = text;

        Measure(text);
        ApplyColours();
    }

    void Measure(string text)
    {
        if (layoutElement == null || label == null)
            return;

        float width = label.GetPreferredValues(text ?? string.Empty).x + horizontalPadding * 2f;

        layoutElement.preferredWidth = Mathf.Max(width, minWidth);
        layoutElement.minWidth = layoutElement.preferredWidth;
        layoutElement.preferredHeight = height;
        layoutElement.minHeight = height;
    }

    public void SetSelected(bool selected)
    {
        _selected = selected;
        ApplyColours();
    }

    void ApplyColours()
    {
        if (background != null)
        {
            background.color = _selected
                ? ThemeManager.Get(ThemeRole.Accent)
                : ThemeManager.Get(ThemeRole.Surface);
        }

        if (label != null)
        {
            label.color = _selected
                ? ThemeManager.Get(ThemeRole.OnAccent)
                : ThemeManager.Get(ThemeRole.TextSecondary);
        }
    }

    void HandleClick()
    {
        if (_onClick != null)
            _onClick();
    }
}
