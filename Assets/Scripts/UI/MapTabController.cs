using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the Map tab: the drawn map, the panel resting on it, and the states where
/// there is nothing to draw.
///
/// This is the screen the app opens on. That is the whole reason it exists - the
/// old first screen was a gate in front of the camera, and a camera that opens
/// itself is a camera pointed at whatever the reader happened to be holding it
/// over. A map answers "what is around me" without asking for anything.
/// </summary>
public class MapTabController : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;
    public MapView map;
    public InfoPanelController infoPanel;

    [Tooltip("Switched to when the reader taps Open camera on a marker.")]
    public TabNavigationController tabs;

    [Header("Panel")]
    [Tooltip("Previous, here, next - the strip resting on the map. It replaced a card " +
             "that named whatever happened to be closest, which is proximity rather " +
             "than the reader's question.")]
    public NextStopStrip nextStop;

    [Header("Chrome")]
    public GuidanceBanner guidance;
    public StateCardView stateCard;

    [Tooltip("Appears only once the reader has panned or zoomed away, so it is not " +
             "a permanent button for a thing that is already true.")]
    public GameObject recentreHolder;

    [Tooltip("The gear at the end of the top row. Settings used to be a tab, which " +
             "spent a third of the only surface visible on every screen on a page " +
             "opened twice in the life of an install.")]
    public Button settingsButton;

    [Header("Browse")]
    [Tooltip("Opens the full list with its filters. The map answers what is around " +
             "you; this answers what exists.")]
    public Button browseButton;
    public TextMeshProUGUI browseLabel;

    [Header("Behaviour")]
    public double radiusMetres = 500d;

    float _timer;

    void Awake()
    {
        if (browseButton != null)
            browseButton.onClick.AddListener(OpenBrowse);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (map != null)
        {
            map.onPinSelected = OpenMarker;
            map.onViewMoved = RefreshRecentre;
        }

        if (stateCard != null)
        {
            stateCard.radiusMetres = radiusMetres;
            stateCard.onAction = HandleStateAction;
        }

        BuildingDatabase.OnBuildingsChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        GuidanceService.OnGuidanceChanged += Refresh;

        Refresh();
    }

    void OnDisable()
    {
        BuildingDatabase.OnBuildingsChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        GuidanceService.OnGuidanceChanged -= Refresh;

        // A sheet left open would be stranded half-drawn behind whichever tab the
        // reader switched to.
        if (infoPanel != null)
            infoPanel.ClosePanel();
    }

    void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < 1f)
            return;

        _timer = 0f;
        Refresh();
    }

    void LateUpdate()
    {
        if (map != null && infoPanel != null)
            map.interactable = infoPanel.Current == null;
    }

    // ------------------------------------------------------------------ actions

    void OpenMarker(BuildingData building)
    {
        if (infoPanel != null && building != null)
            infoPanel.ShowPanel(building);
    }

    /// <summary>
    /// Brings a marker chosen elsewhere onto the map: selects its pin, moves the
    /// view so it is actually on screen, and opens its sheet.
    /// </summary>
    public void Focus(BuildingData building)
    {
        if (building == null)
            return;

        if (map != null)
        {
            map.Select(building);

            // Without this the sheet names a marker whose pin is somewhere off the
            // edge, which reads as the map ignoring the tap.
            map.Recentre();

            if (locationService != null && locationService.HasValidPose)
            {
                double distance = locationService.DistanceMetresTo(building);
                if (distance > 0d)
                {
                    map.metresAcross = Mathf.Clamp((float)distance * 2.5f,
                                                   map.minMetresAcross, map.maxMetresAcross);
                }
            }
        }

        OpenMarker(building);
    }

    void OpenBrowse()
    {
        if (tabs != null)
            tabs.ShowBrowse();
    }

    /// <summary>
    /// The empty state widens the search rather than retrying it, because retrying
    /// a request that succeeded and returned nothing does nothing twice.
    /// </summary>
    void HandleStateAction()
    {
        if (stateCard == null)
            return;

        if (stateCard.Current == StateCardView.Kind.Empty)
        {
            OpenBrowse();
            return;
        }

        RemoteMarkerSource source = FindAnyObjectByType<RemoteMarkerSource>();
        if (source != null)
            source.Refresh();
    }

    // ------------------------------------------------------------------ drawing

    void Refresh()
    {
        // The trail's name is deliberately not set here. TrailChipView owns that
        // label and knows which trail is open; this line used to overwrite it with
        // "Map" on every refresh, so the name the top bar exists to show would
        // appear once and then be replaced by the word the tab bar already says
        // two inches below it.

        if (browseLabel != null)
            browseLabel.text = AppStrings.Get("map.browse");

        RefreshNearest();
        RefreshState();
        RefreshRecentre();

        if (guidance != null)
            guidance.Refresh();
    }

    /// <summary>
    /// Hands the dock to the strip, and takes it away while guidance is running.
    ///
    /// The guidance banner already names the marker the reader is walking to. A
    /// strip naming a different one underneath it is two answers to one question,
    /// which is the same reason the card it replaced hid here.
    /// </summary>
    void RefreshNearest()
    {
        if (nextStop == null)
            return;

        bool show = !GuidanceService.IsGuiding;

        if (nextStop.gameObject.activeSelf != show)
            nextStop.gameObject.SetActive(show);

        if (show)
            nextStop.Refresh();
    }

    void RefreshState()
    {
        if (stateCard == null)
            return;

        bool located = locationService != null && locationService.HasValidPose;
        int count = BuildingDatabase.AllBuildings.Count;

        // Ordered by what the reader can act on. No position is the only one that
        // resolves itself by waiting, so it is checked first and offers nothing to
        // press.
        if (!located)
        {
            stateCard.Show(StateCardView.Kind.Locating);
            return;
        }

        if (count > 0)
        {
            stateCard.Hide();
            return;
        }

        if (BuildingDatabase.LoadState == BuildingDatabase.State.Failed)
        {
            stateCard.Show(Application.internetReachability ==
                           NetworkReachability.NotReachable
                ? StateCardView.Kind.Offline
                : StateCardView.Kind.Failed);
            return;
        }

        if (BuildingDatabase.LoadState == BuildingDatabase.State.Ready)
        {
            stateCard.Show(StateCardView.Kind.Empty);
            return;
        }

        // Position is known and the list is still coming. Saying "finding your
        // position" here would be describing something that already finished.
        stateCard.Show(StateCardView.Kind.Loading);
    }

    void RefreshRecentre()
    {
        if (recentreHolder != null && map != null)
            recentreHolder.SetActive(!map.FollowsReader);
    }
}
