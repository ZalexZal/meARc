using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Which screen is on.
///
/// Three tabs, in the order the walk happens: Map, Camera, Settings. The app opens
/// on Map, never on Camera - a camera that opens itself is pointed at whatever the
/// phone happened to be over, and it asks for the one permission the reader has no
/// reason to grant yet.
///
/// Browse is a fourth panel with no tab of its own. It belongs to Map - the map
/// answers "what is around me", browse answers "what exists" - and giving it a tab
/// would put a fourth thing in a bar whose job is to be readable at a glance while
/// walking.
/// </summary>
public class TabNavigationController : MonoBehaviour
{
    [Header("Panels")]
    public GameObject mapPanel;
    public GameObject cameraPanel;
    public GameObject settingsPanel;

    [Tooltip("The searchable list. Reached from the Map tab, not from the nav bar.")]
    public GameObject browsePanel;

    [Header("Chrome")]
    public GameObject bottomNav;

    [Tooltip("The floating glass bar. When it is set it draws the open tab itself, " +
             "and the labels, icons and pills below are not used.")]
    public GlassTabBar glass;

    [Header("Tabs")]
    public SettingsTabController settingsTab;

    [Header("Tab labels")]
    [Tooltip("Coloured here rather than by a ThemedElement, because the active tab " +
             "and the inactive ones need different roles.")]
    public TMP_Text mapLabel;
    public TMP_Text cameraLabel;
    public TMP_Text settingsLabel;

    [Header("Tab icons")]
    public Graphic[] mapIcon;
    public Graphic[] cameraIcon;
    public Graphic[] settingsIcon;

    [Header("Active indicator")]
    [Tooltip("The shape that moves to the open tab. Colour and weight alone left " +
             "the bar reading as three grey pictures with one of them tinted.")]
    public Graphic mapIndicator;
    public Graphic cameraIndicator;
    public Graphic settingsIndicator;

    enum Tab
    {
        Map,
        Camera,
        Settings
    }

    Tab _tab = Tab.Map;

    /// <summary>True while the Camera tab is the one on screen.</summary>
    public static bool CameraOpen { get; private set; }

    /// <summary>Raised after any tab or the list is shown.</summary>
    public static event System.Action OnTabShown;

    void Start()
    {
        ShowMap();
    }

    void OnEnable()
    {
        ThemeManager.OnThemeChanged += ApplyTabColours;
        ApplyTabColours();
    }

    void OnDisable()
    {
        ThemeManager.OnThemeChanged -= ApplyTabColours;
    }

    public void ShowMap()
    {
        _tab = Tab.Map;
        ShowOnly(mapPanel);
    }

    public void ShowCamera()
    {
        _tab = Tab.Camera;
        ShowOnly(cameraPanel);
    }

    public void ShowSettings()
    {
        _tab = Tab.Settings;
        ShowOnly(settingsPanel);

        // OnEnable does this on a fresh open, but pressing Settings while already
        // on Settings never disables the panel, so nothing would reset.
        if (settingsTab != null)
            settingsTab.ResetToRoot();
    }

    /// <summary>
    /// The list, shown over the Map tab. The Map tab stays lit, because that is
    /// where Back goes, and a nav bar that lights nothing leaves the reader
    /// wondering where they are.
    /// </summary>
    public void ShowBrowse()
    {
        _tab = Tab.Map;
        ShowOnly(browsePanel);
    }

    void ShowOnly(GameObject panelToShow)
    {
        if (bottomNav != null)
            bottomNav.SetActive(true);

        if (glass != null)
            glass.ShowActive((int)_tab, true);

        SetActive(mapPanel, panelToShow);
        SetActive(cameraPanel, panelToShow);
        SetActive(settingsPanel, panelToShow);
        SetActive(browsePanel, panelToShow);

        ApplyTabColours();

        CameraOpen = _tab == Tab.Camera;
        if (OnTabShown != null)
            OnTabShown();
    }

    static void SetActive(GameObject panel, GameObject wanted)
    {
        if (panel != null)
            panel.SetActive(panel == wanted);
    }

    void ApplyTabColours()
    {
        if (glass != null)
            return;

        Apply(mapLabel, mapIcon, mapIndicator, _tab == Tab.Map);
        Apply(cameraLabel, cameraIcon, cameraIndicator, _tab == Tab.Camera);
        Apply(settingsLabel, settingsIcon, settingsIndicator, _tab == Tab.Settings);
    }

    /// <summary>
    /// The active tab is shown by colour and by weight, not by colour alone -
    /// bold is the cue that still works for anyone who cannot tell this teal from
    /// this grey.
    /// </summary>
    static void Apply(TMP_Text label, Graphic[] icon, Graphic indicator, bool active)
    {
        Color colour = ThemeManager.Get(active ? ThemeRole.Accent : ThemeRole.TextMuted);

        if (label != null)
        {
            label.color = colour;
            label.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
        }

        if (indicator != null)
        {
            // A filled shape behind the icon, not just a tint. It is the cue that
            // survives being read at arm's length while walking, and the one that
            // works for anyone who cannot tell this teal from this grey.
            Color fill = ThemeManager.Get(ThemeRole.Accent);
            fill.a = active ? 0.16f : 0f;
            indicator.color = fill;
        }

        if (icon == null)
            return;

        for (int i = 0; i < icon.Length; i++)
        {
            if (icon[i] != null)
                icon[i].color = colour;
        }
    }
}
