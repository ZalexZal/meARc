using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The calibration dot and its explainer bubble. Colour is the glanceable signal;
/// the bubble is there for the reader who wants to know why markers are drifting.
/// </summary>
public class VpsIndicatorController : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;
    public Button dotButton;
    public Image dotImage;

    [Tooltip("The word beside the dot. A coloured circle on its own is a colour, " +
             "not a status - it needs reading twice and fails entirely for anyone " +
             "who cannot separate the greens from the ambers.")]
    public TextMeshProUGUI dotLabel;

    [Header("Bubble")]
    [Tooltip("Transparent catcher below the bubble, so tapping anywhere else " +
             "dismisses it.")]
    public GameObject scrim;
    public Button scrimButton;

    public GameObject bubbleRoot;
    public Image bubbleDot;
    public TextMeshProUGUI bubbleTitle;
    public TextMeshProUGUI bubbleBody;
    public TextMeshProUGUI bubbleAccuracy;

    VpsStatus _status = VpsStatus.Unavailable;

    void Awake()
    {
        if (dotButton != null)
            dotButton.onClick.AddListener(ToggleBubble);

        if (scrimButton != null)
            scrimButton.onClick.AddListener(CloseBubble);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (locationService != null)
        {
            locationService.OnStatusChanged += HandleStatusChanged;
            _status = locationService.Status;
        }

        AppStrings.OnLanguageChanged += Refresh;
        ThemeManager.OnThemeChanged += Refresh;

        if (bubbleRoot != null)
            bubbleRoot.SetActive(false);

        if (scrim != null)
            scrim.SetActive(false);

        Refresh();
    }

    void OnDisable()
    {
        if (locationService != null)
            locationService.OnStatusChanged -= HandleStatusChanged;

        AppStrings.OnLanguageChanged -= Refresh;
        ThemeManager.OnThemeChanged -= Refresh;
    }

    void HandleStatusChanged(VpsStatus status)
    {
        _status = status;
        Refresh();
    }

    public void ToggleBubble()
    {
        if (bubbleRoot == null)
            return;

        bubbleRoot.SetActive(!bubbleRoot.activeSelf);

        if (scrim != null)
            scrim.SetActive(bubbleRoot.activeSelf);

        if (bubbleRoot.activeSelf)
            Refresh();
    }

    public void CloseBubble()
    {
        if (bubbleRoot != null)
            bubbleRoot.SetActive(false);

        if (scrim != null)
            scrim.SetActive(false);
    }

    void Refresh()
    {
        Color colour = ThemeManager.Get(RoleFor(_status));

        if (dotImage != null)
            dotImage.color = colour;

        string key = KeyFor(_status);

        // Written whether or not the bubble is open: this is the chip on the
        // camera screen, and it is the only calibration state most readers see.
        // It sat below the return that follows, so the chip said what the
        // builder wrote - "Cannot calibrate here", in English - whatever the
        // camera knew and whatever the language.
        if (dotLabel != null)
            dotLabel.text = AppStrings.Get(key + ".title");

        if (bubbleRoot == null || !bubbleRoot.activeSelf)
            return;

        if (bubbleDot != null)
            bubbleDot.color = colour;

        if (bubbleTitle != null)
            bubbleTitle.text = AppStrings.Get(key + ".title");

        if (bubbleBody != null)
            bubbleBody.text = AppStrings.Get(key + ".body");

        if (bubbleAccuracy != null)
        {
            bool hasPose = locationService != null && locationService.HasValidPose;

            bubbleAccuracy.text = hasPose
                ? string.Format(AppStrings.Get("vps.accuracy"),
                                locationService.HorizontalAccuracy.ToString("0.0"))
                : AppStrings.Get("vps.unavailable");
        }
    }

    static ThemeRole RoleFor(VpsStatus status)
    {
        switch (status)
        {
            case VpsStatus.Good: return ThemeRole.StatusGood;
            case VpsStatus.Fair: return ThemeRole.StatusFair;
            case VpsStatus.Poor: return ThemeRole.StatusPoor;
            default: return ThemeRole.StatusNone;
        }
    }

    static string KeyFor(VpsStatus status)
    {
        switch (status)
        {
            case VpsStatus.Good: return "vps.good";
            case VpsStatus.Fair: return "vps.fair";
            case VpsStatus.Poor: return "vps.poor";
            default: return "vps.none";
        }
    }
}
