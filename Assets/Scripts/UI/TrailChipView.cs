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

    void Awake()
    {
        if (button != null)
            button.onClick.AddListener(Open);
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

        if (nameLabel != null)
        {
            nameLabel.text = trail != null && !string.IsNullOrWhiteSpace(trail.name)
                ? trail.name
                : AppStrings.Get("tab.map");
        }

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

        // Nothing to swap to when the reader holds one trail, so the chevron - the
        // only thing saying this is pressable - comes off with it.
        bool switchable = service != null && service.Mine.Count > 1;

        if (chevron != null)
            chevron.SetActive(switchable);

        if (button != null)
            button.interactable = switchable;
    }
}
