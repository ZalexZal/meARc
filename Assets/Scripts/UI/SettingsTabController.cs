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

    [Tooltip("Says that this fires while the app is open. Without it the row sits " +
             "under a heading that promises something it does not do.")]
    public TextMeshProUGUI nearbyNoteLabel;

    [Header("Privacy")]
    [Tooltip("Anyone can put a marker on a public street, so the reader needs a way " +
             "to take one off their own map. This is where they get it back.")]
    public TextMeshProUGUI privacyLabel;
    public TextMeshProUGUI hiddenRowLabel;
    public TextMeshProUGUI hiddenCountLabel;
    public Button unhideAllButton;
    public TextMeshProUGUI unhideAllLabel;

    [Header("Help")]
    public TextMeshProUGUI tutorialRowLabel;
    public TextMeshProUGUI sourcesRowLabel;
    public TextMeshProUGUI aboutRowLabel;
    public Button tutorialButton;
    public Button sourcesButton;
    public Button aboutButton;

    [Header("Help")]
    [Tooltip("The walkthrough itself. Replay tutorial opens it rather than " +
             "navigating to a page about it.")]
    public TutorialFlow tutorial;

    [Header("Sub-pages")]
    public SubPanelNavigator navigator;

    [Header("You")]
    [Tooltip("Opens the page with every badge and its rule.")]
    public Button allBadgesButton;
    public GameObject badgesPage;

    [Tooltip("Opens the email and password page. Only on screen with an account.")]
    public Button securityButton;
    public GameObject securityPage;

    [Tooltip("Opens the list of people this reader has blocked.")]
    public Button blockedButton;
    public GameObject blockedPage;

    [Tooltip("Open the website's own pages, in the app's language.")]
    public Button termsButton;
    public Button privacyButton;

    [Tooltip("The settings list's own scroll, put back to the top on every open.")]
    public ScrollRect scroll;
    public GameObject sourcesPage;
    public GameObject aboutPage;

    [Header("Footer")]
    public TextMeshProUGUI versionLabel;

    [Tooltip("Seven taps on the version label toggle developer mode, which reveals " +
             "draft buildings. Same gesture Android uses for Developer Options.")]
    public Button versionButton;

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
            nearbyToggle.isOn = ProximityAlerts.Enabled;
            nearbyToggle.onValueChanged.AddListener(SetNearbyAlerts);
        }

        if (tutorialButton != null)
            tutorialButton.onClick.AddListener(ReplayTutorial);

        if (versionButton != null)
            versionButton.onClick.AddListener(HandleVersionTap);

        if (unhideAllButton != null)
            unhideAllButton.onClick.AddListener(UnhideAll);

        WireHelpRow(sourcesButton, sourcesPage);
        WireHelpRow(aboutButton, aboutPage);
        WireHelpRow(allBadgesButton, badgesPage);
        WireHelpRow(securityButton, securityPage);
        WireHelpRow(blockedButton, blockedPage);

        if (termsButton != null)
            termsButton.onClick.AddListener(() => Application.OpenURL(SiteLink.Terms()));

        if (privacyButton != null)
            privacyButton.onClick.AddListener(() => Application.OpenURL(SiteLink.Privacy()));
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
        HiddenMarkers.OnChanged += ApplyLabels;

        ApplyLabels();

        if (navigator != null)
            navigator.ShowRoot();

        // Back to the top, every time.
        //
        // Independent of why the list would not scroll up: a settings page that
        // reopens halfway down is showing somebody the middle of a document they
        // have not read, and if scrolling up ever fails again this is what keeps
        // the account card reachable.
        if (scroll != null)
            scroll.verticalNormalizedPosition = 1f;
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= ApplyLabels;
        ThemeManager.OnThemeChanged -= ApplyLanguageSegment;
        HiddenMarkers.OnChanged -= ApplyLabels;
    }

    void UnhideAll()
    {
        HiddenMarkers.UnhideAll();
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

    static void SetNearbyAlerts(bool enabled)
    {
        ProximityAlerts.Enabled = enabled;
    }

    /// <summary>The demo replaced the tutorial, so the replay is the demo, on the map.</summary>
    void ReplayTutorial()
    {
        TabNavigationController tabs = FindAnyObjectByType<TabNavigationController>();
        if (tabs != null)
            tabs.ShowMap();

        if (DoorFlow.Instance != null)
            DoorFlow.Instance.StartDemo();
        else if (tutorial != null)
            tutorial.Open();
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
        Set(nearbyNoteLabel, "settings.nearbyNote");
        Set(privacyLabel, "settings.privacy");
        Set(hiddenRowLabel, "settings.hidden");
        Set(unhideAllLabel, "settings.unhideAll");
        Set(helpLabel, "settings.help");

        int hidden = HiddenMarkers.Count;

        if (hiddenCountLabel != null)
        {
            hiddenCountLabel.text = hidden == 0
                ? AppStrings.Get("settings.hiddenNone")
                : string.Format(AppStrings.Get("settings.hiddenCount"), hidden);
        }

        // Nothing to show again, so the control is not offered rather than offered
        // and dead.
        if (unhideAllButton != null)
            unhideAllButton.gameObject.SetActive(hidden > 0);
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
