using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings, plus the sub-page navigation behind the Help rows. Every control here
/// writes its choice to PlayerPrefs immediately - there is no Apply button and
/// nothing to lose by backgrounding the app.
/// </summary>
public class SettingsTabController : MonoBehaviour
{
    [Header("Section labels")]
    public TextMeshProUGUI appearanceLabel;
    public TextMeshProUGUI languageSectionLabel;
    public TextMeshProUGUI notificationsLabel;
    public TextMeshProUGUI helpLabel;

    [Header("Appearance")]
    public TextMeshProUGUI themeRowLabel;
    public Toggle themeToggle;

    [Tooltip("Lets a reader turn occlusion off where the surroundings aren't mapped " +
             "well enough for it to help.")]
    public TextMeshProUGUI occlusionRowLabel;
    public Toggle occlusionToggle;

    [Header("Language")]
    public TextMeshProUGUI languageRowLabel;
    public Button englishButton;
    public Button bulgarianButton;
    public Image englishBackground;
    public Image bulgarianBackground;
    public TextMeshProUGUI englishLabel;
    public TextMeshProUGUI bulgarianLabel;

    [Header("Notifications")]
    public TextMeshProUGUI nearbyRowLabel;
    public Toggle nearbyToggle;

    [Header("Help")]
    public TextMeshProUGUI tutorialRowLabel;
    public TextMeshProUGUI sourcesRowLabel;
    public TextMeshProUGUI aboutRowLabel;
    public Button tutorialButton;
    public Button sourcesButton;
    public Button aboutButton;

    [Header("Sub-pages")]
    public SubPanelNavigator navigator;
    public GameObject tutorialPage;
    public GameObject sourcesPage;
    public GameObject aboutPage;

    [Header("Footer")]
    public TextMeshProUGUI versionLabel;

    [Tooltip("Seven taps on the version label toggle developer mode, which reveals " +
             "draft buildings. Same gesture Android uses for Developer Options.")]
    public Button versionButton;

    const string NearbyPrefKey = "notify_nearby";

    void Awake()
    {
        if (themeToggle != null)
        {
            themeToggle.isOn = ThemeManager.IsDark;
            themeToggle.onValueChanged.AddListener(ThemeManager.SetDark);
        }

        if (occlusionToggle != null)
        {
            occlusionToggle.isOn = OcclusionService.Enabled;
            occlusionToggle.onValueChanged.AddListener(SetOcclusion);
        }

        if (englishButton != null)
            englishButton.onClick.AddListener(() => SetLanguage(AppLanguage.English));

        if (bulgarianButton != null)
            bulgarianButton.onClick.AddListener(() => SetLanguage(AppLanguage.Bulgarian));

        if (nearbyToggle != null)
        {
            nearbyToggle.isOn = PlayerPrefs.GetInt(NearbyPrefKey, 1) == 1;
            nearbyToggle.onValueChanged.AddListener(SetNearbyNotifications);
        }

        if (versionButton != null)
            versionButton.onClick.AddListener(HandleVersionTap);

        WireHelpRow(tutorialButton, tutorialPage);
        WireHelpRow(sourcesButton, sourcesPage);
        WireHelpRow(aboutButton, aboutPage);
    }

    /// <summary>
    /// Returns to the top level of Settings. Called when the Settings tab button is
    /// pressed, so that button always means "the Settings screen" rather than
    /// leaving you wherever you drilled to last.
    /// </summary>
    public void ResetToRoot()
    {
        if (navigator != null)
            navigator.ShowRoot();
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += ApplyLabels;
        ThemeManager.OnThemeChanged += ApplyLanguageSegment;

        ApplyLabels();

        if (navigator != null)
            navigator.ShowRoot();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= ApplyLabels;
        ThemeManager.OnThemeChanged -= ApplyLanguageSegment;
    }

    void WireHelpRow(Button button, GameObject page)
    {
        if (button == null || navigator == null || page == null)
            return;

        button.onClick.AddListener(() => navigator.ShowPage(page));
    }

    static void SetOcclusion(bool enabled)
    {
        OcclusionService.Enabled = enabled;
    }

    void HandleVersionTap()
    {
        if (DeveloperMode.RegisterTap() != 0)
            return;

        // Drafts becoming visible or hidden changes what every list contains.
        BuildingDatabase.RebuildVisible();
        ApplyLabels();
    }

    void SetLanguage(AppLanguage language)
    {
        AppStrings.SetLanguage(language);
        ApplyLanguageSegment();
    }

    // This only records the preference for now. Scheduling is a later pass.
    void SetNearbyNotifications(bool enabled)
    {
        PlayerPrefs.SetInt(NearbyPrefKey, enabled ? 1 : 0);
        PlayerPrefs.Save();
    }

    void ApplyLabels()
    {
        Set(appearanceLabel, "settings.appearance");
        Set(themeRowLabel, "settings.theme");
        Set(occlusionRowLabel, "settings.occlusion");
        Set(languageSectionLabel, "settings.language");
        Set(languageRowLabel, "settings.appLanguage");
        Set(notificationsLabel, "settings.notifications");
        Set(nearbyRowLabel, "settings.nearbyBuildings");
        Set(helpLabel, "settings.help");
        Set(tutorialRowLabel, "settings.replayTutorial");
        Set(sourcesRowLabel, "settings.dataSources");
        Set(aboutRowLabel, "settings.about");

        if (versionLabel != null)
        {
            string version = string.Format(AppStrings.Get("settings.version"), Application.version);

            // The only visible sign developer mode is on, which is the point.
            versionLabel.text = DeveloperMode.Enabled ? version + "  ·  dev" : version;
        }

        ApplyLanguageSegment();
    }

    static void Set(TextMeshProUGUI label, string key)
    {
        if (label != null)
            label.text = AppStrings.Get(key);
    }

    /// <summary>Highlights whichever half of the EN/BG segmented control is active.</summary>
    void ApplyLanguageSegment()
    {
        bool english = AppStrings.CurrentLanguage == AppLanguage.English;

        Color accent = ThemeManager.Get(ThemeRole.Accent);
        Color onAccent = ThemeManager.Get(ThemeRole.OnAccent);
        Color surface = ThemeManager.Get(ThemeRole.Surface);
        Color muted = ThemeManager.Get(ThemeRole.TextMuted);

        if (englishBackground != null) englishBackground.color = english ? accent : surface;
        if (bulgarianBackground != null) bulgarianBackground.color = english ? surface : accent;
        if (englishLabel != null) englishLabel.color = english ? onAccent : muted;
        if (bulgarianLabel != null) bulgarianLabel.color = english ? muted : onAccent;
    }
}
