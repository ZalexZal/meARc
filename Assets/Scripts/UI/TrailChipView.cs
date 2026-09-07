using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Which trail is open, at the top of the map, and the way to change it.
///
/// This replaced a title that said "Map" - a word the tab bar was already saying
/// two inches below it. The trail's name is the thing a reader standing in a
/// street actually cannot work out from the screen, and once a reader can hold
/// more than one trail there has to be somewhere to swap between them.
/// </summary>
public class TrailChipView : MonoBehaviour
{
    public Button button;
    public TextMeshProUGUI nameLabel;

    [Tooltip("Freefall, Capture the flag, and so on. Hidden for a plain guided " +
             "trail, where naming the mode tells nobody anything.")]
    public GameObject modeChip;

    public TextMeshProUGUI modeLabel;

    [Tooltip("The chevron that says this is a control rather than a heading.")]
    public GameObject chevron;

    [Tooltip("Reports the whole trail. Its own hit area on top of the chip's, " +
             "because the chip itself opens the trail list.")]
    public Button reportButton;

    [Tooltip("The chooser it opens. Shared with the one on a marker's sheet.")]
    public ReportSheetView reportSheet;

    void Awake()
    {
        if (button != null)
            button.onClick.AddListener(Open);

        if (reportButton != null)
            reportButton.onClick.AddListener(Report);
    }

    /// <summary>
    /// Reports the trail rather than a marker on it.
    ///
    /// Hidden when no trail is open, since there would be nothing to report -
    /// Refresh handles that alongside everything else the chip shows.
    /// </summary>
    void Report()
    {
        TrailService trails = TrailService.Instance;

        if (reportSheet != null && trails != null && !string.IsNullOrEmpty(trails.SelectedId))
            reportSheet.OpenTrail(trails.SelectedId);
    }

    void OnEnable()
    {
        TrailService.OnTrailsChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        TrailService.OnTrailsChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
    }

    void Open()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenTrails();
    }

    public void Refresh()
    {
        TrailService service = TrailService.Instance;
        TrailService.Trail trail = service != null ? service.Selected : null;

        bool hasName = trail != null && !string.IsNullOrWhiteSpace(trail.name);

        // With no trail open this is an invitation, not a heading. It used to read
        // "Map" - the word the tab bar says two inches below it, and one that tells
        // a reader holding no trail nothing about the single thing left to do.
        if (nameLabel != null)
            nameLabel.text = hasName ? trail.name : AppStrings.Get("trails.choose");

        // Nothing open, nothing to report.
        if (reportButton != null)
            reportButton.gameObject.SetActive(hasName);

        // Guide is the default and needs no label. A chip on every trail would be a
        // chip that means nothing.
        string mode = trail != null ? trail.mode : null;
        bool worthSaying = !string.IsNullOrEmpty(mode) && mode != "guide";

        if (modeChip != null)
            modeChip.SetActive(worthSaying);

        if (worthSaying && modeLabel != null)
        {
            string named = AppStrings.Get("mode." + mode);

            // Get hands back the key when a string is missing, which would put
            // "mode.relay" on screen. Show the raw value instead - it is at least
            // a word, and a mode this build does not know about is the portal
            // being ahead of the app rather than a fault.
            modeLabel.text = named.StartsWith("mode.") ? mode : named;
        }

        // Always pressable.
        //
        // It was interactable only with more than one trail, or none - so holding
        // exactly one made it dead, and My trails is the only route to joining a
        // second or leaving the one you have. A reader who joins one trail could
        // not reach that screen again by any path in the app.
        //
        // The chevron follows the same rule now. It said "there is somewhere to
        // go" and there always is: the list is worth opening to leave a trail even
        // when there is only one in it.
        bool switchable = service != null;

        if (chevron != null)
            chevron.SetActive(switchable);

        if (button != null)
            button.interactable = switchable;
    }
}
