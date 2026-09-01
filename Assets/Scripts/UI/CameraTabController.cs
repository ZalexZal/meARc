using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

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
/// On the AR session: it starts switched off and is brought up the first time the
/// reader opens this tab. It is never switched off again after that, because
/// turning it off would stop Geospatial localization and localization is what the
/// reader is waiting for.
///
/// The delayed start is not a preference. ARCore claims the camera the moment the
/// session is enabled, and the session used to be enabled in the scene from
/// launch - so a fresh install put a camera permission dialog in front of somebody
/// who had not seen the app yet, and the activity was finished inside that flow.
/// The app closed on first launch and worked on the second, because by then the
/// permission was granted and nothing prompted. A map app has no business opening
/// the camera before it has drawn a map.
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

    [Tooltip("Off until the reader opens this tab for the first time, and left " +
             "running from then on - see the note on this class.")]
    public ARSession arSession;

    [Tooltip("Switched off while the reader is on another tab. The session keeps " +
             "running and keeps localizing; only the blit stops.")]
    public ARCameraBackground cameraBackground;

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

    [Tooltip("Says whether the marker in the dock is one the camera can currently " +
             "see, or merely the closest. Without it the reader cannot tell why " +
             "the street looks empty.")]
    public TextMeshProUGUI dockCaption;
    public BearingArrow dockArrow;
    public TextMeshProUGUI dockEmptyLabel;

    [Header("Off-screen markers")]
    [Tooltip("The disc in the corner. It replaced two edge tabs that could name " +
             "one marker each way and nothing behind.")]
    public CameraRadar radar;

    [Header("Guidance")]
    public GuidanceBanner guidance;

    bool _warningDismissed;
    BuildingData _selected;
    float _timer;

    /// <summary>True while a permission dialog is up, so it is asked for once.</summary>
    bool _asking;

    /// <summary>Set when the reader has said no, so the tab explains itself.</summary>
    bool _cameraRefused;

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

        if (infoPanel != null)
            infoPanel.onWillOpen = CloseNearby;

        // Before anything can enable it. Awake runs while the app is still opening
        // and this tab is not the one on screen, so the session must not be the
        // thing that decides when the camera is claimed.
        if (arSession != null)
            arSession.enabled = false;
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

        SetFeedVisible(true);

        // Whatever the reader is walking to is what the camera should be talking
        // about when they raise the phone.
        if (GuidanceService.IsGuiding)
            _selected = GuidanceService.Target;

        WakeCamera();
        Refresh();
    }

    // ------------------------------------------------------------- the camera

    /// <summary>
    /// Brings the AR session up, asking for the camera first if it has to.
    ///
    /// Called from OnEnable rather than from Awake, so the reader is looking at
    /// the Camera tab when the dialog appears and has just asked for the camera
    /// themselves. That is the whole difference between a permission request and
    /// an ambush.
    /// </summary>
    void WakeCamera()
    {
        if (arSession == null || arSession.enabled || _asking)
            return;

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
        {
            _asking = true;

            PermissionCallbacks callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => { _asking = false; _cameraRefused = false; StartSession(); };
            callbacks.PermissionDenied += _ => { _asking = false; RefuseCamera(); };
            callbacks.PermissionDeniedAndDontAskAgain += _ => { _asking = false; RefuseCamera(); };

            Permission.RequestUserPermission(Permission.Camera, callbacks);
            return;
        }
#endif

        StartSession();
    }

    void StartSession()
    {
        if (arSession != null)
            arSession.enabled = true;

        SetFeedVisible(isActiveAndEnabled);
    }

    /// <summary>
    /// The reader said no. The map still works, so this says what is missing and
    /// why rather than leaving a black rectangle with chrome floating on it.
    /// </summary>
    void RefuseCamera()
    {
        _cameraRefused = true;
        _warningDismissed = false;
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

        // The camera feed is a full-screen texture drawn every frame. On the map
        // it is behind an opaque panel and nobody can see it, so it stops - but the
        // session stays up, because localization is the slow part and losing it
        // would mean starting again on every tab switch.
        SetFeedVisible(false);

        // Leaving the tab with a sheet open would strand it half-drawn behind
        // whichever tab the reader switched to.
        CloseNearby();

        if (infoPanel != null)
            infoPanel.ClosePanel();

        if (vpsIndicator != null)
            vpsIndicator.CloseBubble();
    }

    void SetFeedVisible(bool visible)
    {
        if (cameraBackground != null)
            cameraBackground.enabled = visible;
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

        // A refused camera outranks poor accuracy. There is no picture at all, so
        // saying the positioning is rough would be answering a question nobody is
        // asking - and it is the one thing here the reader can actually fix.
        bool refused = _cameraRefused;

        warningBanner.SetActive((refused || poor) && !_warningDismissed);

        if (warningLabel != null)
        {
            warningLabel.text = AppStrings.Get(refused ? "camera.refused"
                                                       : "live.warning");
        }
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

        if (dockCaption != null)
            dockCaption.gameObject.SetActive(has);

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

        if (dockCaption != null)
            dockCaption.text = AppStrings.Get(InView(_selected) ? "camera.inView"
                                                                : "camera.nearest");

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

    /// <summary>
    /// Whether the camera is pointing close enough at a marker to be showing it.
    /// Roughly a phone camera's field of view, halved.
    /// </summary>
    bool InView(BuildingData marker)
    {
        if (marker == null || locationService == null ||
            !locationService.HasValidPose || !locationService.HasHeading)
        {
            return false;
        }

        double bearing = GeoMath.BearingDegrees(
            locationService.Latitude, locationService.Longitude,
            marker.latitude, marker.longitude);

        return Mathf.Abs(Mathf.DeltaAngle(locationService.DeviceHeading, (float)bearing)) <= 31f;
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
