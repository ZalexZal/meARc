using System.Collections;
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
/// On the AR session: it starts switched off and is brought up the first time the
/// reader opens this tab. It is never switched off again after that, because
/// turning it off would stop Geospatial localization and localization is what the
/// reader is waiting for.
///
/// The delayed start is not a preference, and it took a long time to understand.
/// The app closed itself on a fresh install and worked on the second launch, and
/// the reason was not the session at all: it was ARCore Extensions, which ran
/// from launch and cannot survive a runtime permission dialog. Any dialog, asked
/// by anyone, at any point - the process dies with a SIGSEGV at address zero on
/// the Unity main thread, inside Unity's own Java message handling. An otherwise
/// identical build with an empty scene survives the same dialog, and so does one
/// with an AR session in it; disabling this one object is what stops it.
///
/// Both are deferred here, so nothing AR-related is running while a permission
/// can still be asked for. It is also right on its own terms: a map app has no
/// business opening the camera before it has drawn a map.
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

    [Tooltip("The ARCore Extensions object, which is what Geospatial runs on. Off " +
             "at launch for the same reason as the session, and for a sharper one: " +
             "it is what crashed on a permission dialog.")]
    public GameObject arExtensions;

    [Tooltip("Geospatial components that live on other objects - XR Origin, mostly - " +
             "and throw every frame if they run without a session. Switched off in " +
             "the saved scene and switched on with the session.")]
    public Behaviour[] arDeferred = new Behaviour[0];

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

    [Tooltip("\"Add a stop here\", for the open trail's owner and editors.")]
    public Button addStopButton;
    public CaptureMode capture;
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

        if (arNoticeContinue != null)
            arNoticeContinue.onClick.AddListener(AcceptArNotice);

        if (arNoticeLearnMore != null)
            arNoticeLearnMore.onClick.AddListener(() => Application.OpenURL(ArLearnMore));

        // Nothing is switched off here.
        //
        // This tab is inactive at launch - the app opens on the map - so Awake
        // does not run until the reader first opens it, which is exactly too late
        // to be deciding what happens at startup. Both AR objects are saved
        // switched off by AppUIBuilder instead, where being right does not depend
        // on anything running.
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
    const string CameraPermission = "android.permission.CAMERA";

    /// <summary>
    /// Google's own words, before the first Geospatial session - its terms for
    /// ARCore require them "in connection with" turning the API on, with a link
    /// to how Play Services for AR handles the data. Once per install.
    /// </summary>
    const string ArNoticeKey = "arcore_notice_seen";
    const string ArLearnMore = "https://support.google.com/ar?p=how-google-play-services-for-ar-handles-your-data";

    [Header("ARCore notice")]
    public GameObject arNotice;
    public Button arNoticeContinue;
    public Button arNoticeLearnMore;

    void WakeCamera()
    {
        if (PlayerPrefs.GetInt(ArNoticeKey, 0) == 0 && arNotice != null)
        {
            arNotice.SetActive(true);
            return;
        }

        // isActiveAndEnabled, not enabled.
        //
        // The session ships on a switched-off GameObject, and a component's own
        // enabled flag stays true on an inactive object - so this read "already
        // running" and returned every time. Opening the Camera tab asked for
        // nothing and started nothing: a black screen with chrome on it.
        if (arSession == null || arSession.isActiveAndEnabled || _asking)
            return;

        if (!AndroidPermissions.Has(CameraPermission))
        {
            _asking = true;
            StartCoroutine(AndroidPermissions.Ask(CameraPermission, granted =>
            {
                _asking = false;

                if (granted)
                {
                    _cameraRefused = false;
                    StartSession();
                }
                else
                {
                    RefuseCamera();
                }
            }));

            return;
        }


        StartSession();
    }

    void AcceptArNotice()
    {
        PlayerPrefs.SetInt(ArNoticeKey, 1);
        PlayerPrefs.Save();

        if (arNotice != null)
            arNotice.SetActive(false);

        WakeCamera();
    }

    void StartSession()
    {
        // The whole object, not just the component: it ships switched off, so
        // enabling the ARSession alone would enable a component on something that
        // is not running.
        if (arSession != null)
        {
            if (!arSession.gameObject.activeSelf)
                arSession.gameObject.SetActive(true);

            arSession.enabled = true;
        }

        // After the session, which is what it attaches to.
        if (arExtensions != null && !arExtensions.activeSelf)
            arExtensions.SetActive(true);

        for (int i = 0; i < arDeferred.Length; i++)
        {
            if (arDeferred[i] != null)
                arDeferred[i].enabled = true;
        }

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

        // Only a refused camera.
        //
        // This used to fire on poor or unavailable positioning as well, which is
        // the exact condition the calibration chip in the other corner is already
        // reporting - and reporting better, with a title, a sentence saying what
        // to do about it, and a colour. So on the one screen with no room to
        // spare, a walker whose positioning was rough got told so twice, in two
        // places, in two different forms of words.
        //
        // The chip keeps that. This keeps the thing the chip has no way to say,
        // which is also the only thing on this screen the reader can actually
        // fix: there is no camera picture because permission was refused.
        warningBanner.SetActive(_cameraRefused && !_warningDismissed);

        if (warningLabel != null)
            warningLabel.text = AppStrings.Get("camera.refused");

        // And the calibration chip goes with it.
        //
        // The same reasoning, one step further than it was taken. Calibration is
        // a statement about how accurately markers can be placed in the camera
        // view; with no camera view there is nothing to place them in, and a
        // walker who has just been told there is nothing to look through does not
        // also need to know it could not have been calibrated. It is the corner
        // of the screen answering a question the screen has already closed.
        if (vpsIndicator != null)
            vpsIndicator.gameObject.SetActive(!_cameraRefused);
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
        if (addStopButton != null)
        {
            // IsOpen, not Active: a refused placing leaves the bar up with
            // nothing in it, and the button stood on top of it.
            addStopButton.gameObject.SetActive(capture != null && !CaptureMode.IsOpen &&
                                               CaptureMode.CanAddHere());
        }

        if (dock == null)
            return;

        // Looked up again by id: a stop from the trail walked before this one is
        // no longer in the database, and the dock went on naming it - "The Eski
        // Mosque, 218 m" on a trail with one stop 13 m away.
        BuildingData current = _selected != null ? BuildingDatabase.GetById(_selected.buildingId) : null;
        _selected = current != null && !HiddenMarkers.IsHidden(current) ? current : Nearest();

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
