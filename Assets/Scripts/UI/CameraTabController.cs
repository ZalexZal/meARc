using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Owns the Camera tab: what sits over the live feed, and which marker the chrome
/// is currently talking about.
///
/// There is no gate in front of the feed any more. The app opens on the map, so
/// the camera is somewhere the reader chose to go, and asking them to press Start
/// once they are already here is a door in the middle of a room. Calibration is
/// still told truthfully - by the dot, and by the banner when accuracy is poor -
/// but it decides what the reader will see, never whether they may look.
///
/// On the AR session: it is deliberately never disabled. Turning the session off
/// would stop Geospatial localization, and localization is what the reader is
/// waiting for.
/// </summary>
public class CameraTabController : MonoBehaviour
{
    [Header("Views")]
    public GameObject liveARView;

    [Header("References")]
    public UserLocationService locationService;
    public NearbyPanelController nearbyPanel;
    public InfoPanelController infoPanel;
    public VpsIndicatorController vpsIndicator;

    [Tooltip("Held only so the session is reachable for debugging. It is never " +
             "disabled - see the note on this class.")]
    public ARSession arSession;

    [Header("Overlays")]
    public Button nearbyButton;
    public TextMeshProUGUI nearbyCountText;
    public GameObject warningBanner;
    public TextMeshProUGUI warningLabel;
    public Button warningDismissButton;

    [Header("Marker dock")]
    [Tooltip("The row at the bottom naming the marker the chrome is talking about. " +
             "Tapping it opens that marker's sheet.")]
    public GameObject dock;
    public Button dockButton;
    public Image dockDot;
    public Image dockRing;
    public TextMeshProUGUI dockName;
    public TextMeshProUGUI dockDistance;
    public BearingArrow dockArrow;
    public TextMeshProUGUI dockEmptyLabel;

    [Header("Off-screen markers")]
    public CameraEdgePills edgePills;

    [Header("Guidance")]
    public GuidanceBanner guidance;

    bool _warningDismissed;
    BuildingData _selected;
    float _timer;

    /// <summary>The marker the dock and the arrow are pointing at.</summary>
    public BuildingData Selected { get { return _selected; } }

    void Awake()
    {
        if (nearbyButton != null)
            nearbyButton.onClick.AddListener(ToggleNearby);

        if (warningDismissButton != null)
            warningDismissButton.onClick.AddListener(DismissWarning);

        if (dockButton != null)
            dockButton.onClick.AddListener(OpenSelected);

        if (nearbyPanel != null)
            nearbyPanel.onBuildingSelected = SelectAndShow;

        if (edgePills != null)
            edgePills.onSelected = Select;

        if (infoPanel != null)
            infoPanel.onWillOpen = CloseNearby;
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (locationService != null)
        {
            locationService.OnPoseUpdated += Refresh;
            locationService.OnStatusChanged += HandleStatusChanged;
        }

        AppStrings.OnLanguageChanged += Refresh;
        BuildingDatabase.OnBuildingsChanged += HandleBuildingsChanged;
        GuidanceService.OnGuidanceChanged += Refresh;

        if (liveARView != null)
            liveARView.SetActive(true);

        // Whatever the reader is walking to is what the camera should be talking
        // about when they raise the phone.
        if (GuidanceService.IsGuiding)
            _selected = GuidanceService.Target;

        Refresh();
    }

    void OnDisable()
    {
        if (locationService != null)
        {
            locationService.OnPoseUpdated -= Refresh;
            locationService.OnStatusChanged -= HandleStatusChanged;
        }

        AppStrings.OnLanguageChanged -= Refresh;
        BuildingDatabase.OnBuildingsChanged -= HandleBuildingsChanged;
        GuidanceService.OnGuidanceChanged -= Refresh;

        // Leaving the tab with a sheet open would strand it half-drawn behind
        // whichever tab the reader switched to.
        CloseNearby();

        if (infoPanel != null)
            infoPanel.ClosePanel();

        if (vpsIndicator != null)
            vpsIndicator.CloseBubble();
    }

    void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < 0.5f)
            return;

        _timer = 0f;
        RefreshDock();
    }

    // ------------------------------------------------------------------ selection

    public void Select(BuildingData building)
    {
        _selected = building;
        RefreshDock();
    }

    /// <summary>Selects and opens the sheet, for a marker chosen from a list.</summary>
    public void SelectAndShow(BuildingData building)
    {
        Select(building);
        ShowBuilding(building);
    }

    void OpenSelected()
    {
        ShowBuilding(_selected);
    }

    public void ShowBuilding(BuildingData building)
    {
        if (building == null)
            return;

        CloseNearby();

        if (infoPanel != null)
            infoPanel.ShowPanel(building);
    }

    void HandleBuildingsChanged()
    {
        // The selected marker may have been hidden or unpublished while the reader
        // was looking at it.
        if (_selected != null && HiddenMarkers.IsHidden(_selected))
            _selected = null;

        Refresh();
    }

    // -------------------------------------------------------------------- warning

    void HandleStatusChanged(VpsStatus status)
    {
        // A change in calibration is new information, so an old dismissal should
        // not silence it - except when it improved, which needs no banner at all.
        if (status != VpsStatus.Good)
            _warningDismissed = false;

        Refresh();
    }

    void DismissWarning()
    {
        _warningDismissed = true;
        RefreshWarning();
    }

    void RefreshWarning()
    {
        if (warningBanner == null)
            return;

        bool poor = locationService != null &&
                    (locationService.Status == VpsStatus.Poor ||
                     locationService.Status == VpsStatus.Unavailable);

        warningBanner.SetActive(poor && !_warningDismissed);

        if (warningLabel != null)
            warningLabel.text = AppStrings.Get("live.warning");
    }

    // ------------------------------------------------------------------- overlays

    public void ToggleNearby()
    {
        if (nearbyPanel == null)
            return;

        if (nearbyPanel.IsOpen)
        {
            nearbyPanel.Close();
            return;
        }

        if (infoPanel != null)
            infoPanel.ClosePanel();

        nearbyPanel.Open();
    }

    public void CloseNearby()
    {
        if (nearbyPanel != null)
            nearbyPanel.Close();
    }

    void Refresh()
    {
        RefreshWarning();
        RefreshNearbyCount();
        RefreshDock();

        if (guidance != null)
            guidance.Refresh();
    }

    void RefreshNearbyCount()
    {
        if (nearbyCountText == null || nearbyPanel == null)
            return;

        nearbyCountText.text = string.Format(
            AppStrings.Get("camera.nearbyCount"), nearbyPanel.NearbyCount);
    }

    /// <summary>
    /// Falls back to the nearest marker when nothing has been chosen, so the dock
    /// is a useful row on arrival rather than an empty one waiting to be filled.
    /// </summary>
    void RefreshDock()
    {
        if (dock == null)
            return;

        if (_selected == null || HiddenMarkers.IsHidden(_selected))
            _selected = Nearest();

        bool has = _selected != null;

        if (dockEmptyLabel != null)
        {
            dockEmptyLabel.gameObject.SetActive(!has);
            dockEmptyLabel.text = AppStrings.Get("camera.noMarkers");
        }

        if (dockName != null)
            dockName.gameObject.SetActive(has);

        if (dockDistance != null)
            dockDistance.gameObject.SetActive(has);

        if (dockButton != null)
            dockButton.interactable = has;

        if (!has)
        {
            if (dockDot != null)
                dockDot.gameObject.SetActive(false);

            if (dockArrow != null)
                dockArrow.SetTarget(null);

            return;
        }

        Color colour = MarkerPalette.Resolve(_selected);

        if (dockDot != null)
        {
            dockDot.gameObject.SetActive(true);
            dockDot.color = colour;
        }

        if (dockRing != null)
            dockRing.color = MarkerPalette.RingFor(colour);

        if (dockName != null)
            dockName.text = _selected.DisplayName;

        if (dockArrow != null)
            dockArrow.SetTarget(_selected);

        if (dockDistance == null)
            return;

        double metres = locationService != null
            ? locationService.DistanceMetresTo(_selected)
            : -1d;

        dockDistance.text = metres < 0d
            ? AppStrings.Get("map.noLocation")
            : AppStrings.FormatDistance(metres,
                locationService != null && locationService.IsApproximate);
    }

    BuildingData Nearest()
    {
        if (locationService == null || !locationService.HasValidPose)
            return null;

        BuildingData best = null;
        double bestDistance = double.MaxValue;

        var all = BuildingDatabase.AllBuildings;
        for (int i = 0; i < all.Count; i++)
        {
            double distance = locationService.DistanceMetresTo(all[i]);
            if (distance >= 0d && distance < bestDistance)
            {
                bestDistance = distance;
                best = all[i];
            }
        }

        return best;
    }
}
