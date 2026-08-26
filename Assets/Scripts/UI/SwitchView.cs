using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Draws a Toggle as a proper switch: a capsule track that changes colour and a
/// knob that slides between the ends.
///
/// Unity's Toggle only knows how to show and hide a graphic, which reads as a
/// rectangle with a square appearing in it rather than as a switch.
/// </summary>
public class SwitchView : MonoBehaviour
{
    [Header("References")]
    public Toggle toggle;
    public Image track;
    public RectTransform knob;
    public Image knobImage;

    [Tooltip("Gap between the knob and the end of the track.")]
    public float inset = 7f;

    void Awake()
    {
        if (toggle != null)
            toggle.onValueChanged.AddListener(Apply);
    }

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += ApplyCurrent;
        ApplyCurrent();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= ApplyCurrent;
    }

    void OnDestroy()
    {
        if (toggle != null)
            toggle.onValueChanged.RemoveListener(Apply);
    }

    void ApplyCurrent()
    {
        Apply(toggle != null && toggle.isOn);
    }

    public void Apply(bool isOn)
    {
        if (track != null)
        {
            track.color = isOn
                ? ThemeManager.Get(ThemeRole.Accent)
                : ThemeManager.Get(ThemeRole.Line);
        }

        if (knobImage != null)
            knobImage.color = Color.white;

        if (knob == null)
            return;

        // Anchoring to the end it travels to keeps the knob correct whatever width
        // the track ends up at.
        float side = isOn ? 1f : 0f;
        knob.anchorMin = new Vector2(side, 0.5f);
        knob.anchorMax = new Vector2(side, 0.5f);
        knob.pivot = new Vector2(side, 0.5f);
        knob.anchoredPosition = new Vector2(isOn ? -inset : inset, 0f);
    }
}
