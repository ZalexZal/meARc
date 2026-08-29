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
    [Tooltip("The card resting on the map naming the nearest marker.")]
    public GameObject nearestCard;
    public Button nearestButton;
    public Image nearestDot;
    public Image nearestRing;
    public TextMeshProUGUI nearestName;
    public TextMeshProUGUI nearestDistance;
    public BearingArrow nearestArrow;
    public TextMeshProUGUI nearestCaption;

    [Header("Chrome")]
    public GuidanceBanner guidance;
    public StateCardView stateCard;
    public TextMeshProUGUI titleText;

    [Tooltip("Appears only once the reader has panned or zoomed away, so it is not " +
             "a permanent button for a thing that is already true.")]
    public GameObject recentreHolder;

    [Header("Browse")]
    [Tooltip("Opens the full list with its filters. The map answers what is around " +
             "you; this answers what exists.")]
    public Button browseButton;
    public TextMeshProUGUI browseLabel;

    [Header("Behaviour")]
    public double radiusMetres = 500d;

    BuildingData _nearest;
    float _timer;

    void Awake()
    {
        if (nearestButton != null)
            nearestButton.onClick.AddListener(OpenNearest);

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

    void OpenNearest()
    {
        OpenMarker(_nearest);

        if (map != null && _nearest != null)
            map.Select(_nearest);
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
        if (titleText != null)
            titleText.text = AppStrings.Get("tab.map");

        if (browseLabel != null)
            browseLabel.text = AppStrings.Get("map.browse");

        RefreshNearest();
        RefreshState();
        RefreshRecentre();

        if (guidance != null)
            guidance.Refresh();
    }

    void RefreshNearest()
    {
        _nearest = null;

        if (locationService != null && locationService.HasValidPose)
        {
            double best = double.MaxValue;

            var all = BuildingDatabase.AllBuildings;
            for (int i = 0; i < all.Count; i++)
            {
                double distance = locationService.DistanceMetresTo(all[i]);
                if (distance >= 0d && distance < best)
                {
                    best = distance;
                    _nearest = all[i];
                }
            }
        }

        // While guidance is running the banner already names a marker, and a second
        // card naming a different one underneath it is two answers to one question.
        bool show = _nearest != null && !GuidanceService.IsGuiding;

        if (nearestCard != null)
            nearestCard.SetActive(show);

        if (!show)
            return;

        Color colour = MarkerPalette.Resolve(_nearest);

        if (nearestDot != null)
            nearestDot.color = colour;

        if (nearestRing != null)
            nearestRing.color = MarkerPalette.RingFor(colour);

        if (nearestName != null)
            nearestName.text = _nearest.DisplayName;

        if (nearestCaption != null)
            nearestCaption.text = AppStrings.Get("map.nearest");

        if (nearestArrow != null)
            nearestArrow.SetTarget(_nearest);

        if (nearestDistance != null)
        {
            double metres = locationService.DistanceMetresTo(_nearest);
            nearestDistance.text = AppStrings.FormatDistance(
                metres, locationService.IsApproximate);
        }
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
