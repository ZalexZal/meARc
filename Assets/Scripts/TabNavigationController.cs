using TMPro;
using UnityEngine;

public class TabNavigationController : MonoBehaviour
{
    [Header("Panels")]
    public GameObject cameraPanel;
    public GameObject searchPanel;
    public GameObject settingsPanel;

    [Header("Chrome")]
    [Tooltip("Restored on every tab switch, in case the Camera tab hid it for its " +
             "Ready State.")]
    public GameObject bottomNav;

    [Header("Settings tab")]
    [Tooltip("Pressing Settings while already inside a sub-page returns to the top " +
             "level, rather than doing nothing.")]
    public SettingsTabController settingsTab;

    [Header("Camera tab")]
    [Tooltip("Decides whether the Camera tab opens in its Ready State or straight " +
             "into the live view. Without one, the panel is just activated as-is.")]
    public CameraTabController cameraTab;

    [Header("Tab labels (optional)")]
    [Tooltip("Coloured here rather than by a ThemedElement, because the active tab " +
             "and the inactive ones need different roles.")]
    public TMP_Text cameraLabel;
    public TMP_Text searchLabel;
    public TMP_Text settingsLabel;

    // All three panels are left enabled in the scene so they can be edited without
    // toggling them on and off, so the starting tab is decided here instead.
    void Start()
    {
        ShowCamera();
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

    public void ShowCamera()
    {
        ShowOnly(cameraPanel);

        // The Camera tab has two states, and which one to show depends on whether
        // the reader has already started this session - so it decides, not us.
        if (cameraTab != null)
            cameraTab.ShowAppropriateState();
    }

    public void ShowSearch()
    {
        ShowOnly(searchPanel);
    }

    public void ShowSettings()
    {
        ShowOnly(settingsPanel);

        // OnEnable does this on a fresh open, but pressing Settings while already
        // on Settings never disables the panel, so nothing would reset.
        if (settingsTab != null)
            settingsTab.ResetToRoot();
    }

    void ShowOnly(GameObject panelToShow)
    {
        // The Camera tab hides this for its Ready State and turns it back on when
        // entering Live. Every other tab always wants it.
        if (bottomNav != null && panelToShow != cameraPanel)
            bottomNav.SetActive(true);

        cameraPanel.SetActive(panelToShow == cameraPanel);
        searchPanel.SetActive(panelToShow == searchPanel);
        settingsPanel.SetActive(panelToShow == settingsPanel);

        ApplyTabColours();
    }

    void ApplyTabColours()
    {
        Apply(cameraLabel, cameraPanel);
        Apply(searchLabel, searchPanel);
        Apply(settingsLabel, settingsPanel);
    }

    static void Apply(TMP_Text label, GameObject panel)
    {
        if (label == null || panel == null)
            return;

        bool active = panel.activeSelf;

        label.color = ThemeManager.Get(active ? ThemeRole.TextPrimary : ThemeRole.TextMuted);
        label.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
    }
}
