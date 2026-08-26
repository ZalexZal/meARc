using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Owns the Camera tab: the Ready/Live state machine, the overlays on the feed, and
/// keeping the two bottom sheets from ever being open at once.
///
/// On the AR session: it is deliberately never disabled. Turning the session off
/// would stop Geospatial localization, which is the exact thing the Ready State
/// exists to wait for - the reader would sit in front of a status card that never
/// improves. Instead the session runs from launch, so ARCore requests location
/// permission and starts localizing immediately, and the Ready State hides the feed
/// simply by being an opaque panel on top of it. Tap input is gated on the live view
/// being active, so nothing behind the panel is reachable.
/// </summary>
public class CameraTabController : MonoBehaviour
{
    [Header("Views")]
    public GameObject readyStateView;
    public GameObject liveARView;
    public ReadyStateView readyState;

    [Header("References")]
    public UserLocationService locationService;
    public NearbyPanelController nearbyPanel;
    public InfoPanelController infoPanel;
    public VpsIndicatorController vpsIndicator;

    [Tooltip("Held only so the session is reachable for debugging. It is never " +
             "disabled - see the note on this class.")]
    public ARSession arSession;

    [Tooltip("Hidden while the Ready State is showing. That screen is full-bleed " +
             "and its buttons sit where the nav bar would be, so the two cannot " +
             "share the bottom of the screen.")]
    public GameObject bottomNav;

    [Header("Live overlays")]
    public Button nearbyButton;
    public TextMeshProUGUI nearbyCountText;
    public Button pauseButton;
    public GameObject warningBanner;
    public TextMeshProUGUI warningLabel;
    public Button warningDismissButton;

    [Header("Behaviour")]
    [Tooltip("Backgrounded for longer than this and the tab returns to the Ready " +
             "State, because tracking has probably degraded.")]
    public float backgroundResetSeconds = 120f;

    bool _hasEnteredLiveThisSession;

    public bool HasEnteredLive { get { return _hasEnteredLiveThisSession; } }

    void Awake()
    {
        if (nearbyButton != null)
            nearbyButton.onClick.AddListener(ToggleNearby);

        if (pauseButton != null)
            pauseButton.onClick.AddListener(EnterReady);

        if (warningDismissButton != null)
            warningDismissButton.onClick.AddListener(HideWarning);

        if (nearbyPanel != null)
            nearbyPanel.onBuildingSelected = ShowBuilding;

        if (infoPanel != null)
            infoPanel.onWillOpen = CloseNearby;

        if (readyState != null)
        {
            readyState.onStart = EnterLive;
            readyState.onStartAnyway = EnterLiveUnverified;
            readyState.onBuildingSelected = EnterLiveWithBuilding;
        }
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (locationService != null)
        {
            locationService.OnPoseUpdated += RefreshNearbyCount;
            locationService.OnStatusChanged += HandleStatusChanged;
        }

        AppStrings.OnLanguageChanged += RefreshLabels;
        AppLifecycleWatcher.OnResumed += HandleResumed;

        ShowAppropriateState();
        RefreshLabels();
    }

    void OnDisable()
    {
        if (locationService != null)
        {
            locationService.OnPoseUpdated -= RefreshNearbyCount;
            locationService.OnStatusChanged -= HandleStatusChanged;
        }

        AppStrings.OnLanguageChanged -= RefreshLabels;
        AppLifecycleWatcher.OnResumed -= HandleResumed;

        // Whatever this tab did to the nav bar, the tab being left must not
        // outlive it - otherwise Search opens with no way back.
        SetNavVisible(true);

        // Leaving the tab with a sheet open would strand it half-drawn behind
        // whichever tab the reader switched to.
        CloseNearby();

        if (vpsIndicator != null)
            vpsIndicator.CloseBubble();
    }

    // ---------------------------------------------------------- state machine

    /// <summary>
    /// Decides Ready or Live on entering the tab. Called by
    /// TabNavigationController rather than blindly activating a fixed panel.
    /// </summary>
    public void ShowAppropriateState()
    {
        // A long spell in the background is consumed here as well as on the event,
        // because this component is disabled while the reader is on another tab and
        // would otherwise never hear about it.
        if (AppLifecycleWatcher.ConsumeBackgroundDuration() > backgroundResetSeconds)
        {
            EnterReady();
            return;
        }

        if (_hasEnteredLiveThisSession)
            EnterLive();
        else
            EnterReady();
    }

    public void EnterReady()
    {
        CloseNearby();

        if (infoPanel != null)
            infoPanel.ClosePanel();

        if (vpsIndicator != null)
            vpsIndicator.CloseBubble();

        HideWarning();

        if (liveARView != null) liveARView.SetActive(false);
        if (readyStateView != null) readyStateView.SetActive(true);

        // Start and "Start anyway" occupy the bottom of the screen, so the nav bar
        // stands down until the reader has started. It comes straight back in Live.
        SetNavVisible(false);
    }

    public void EnterLive()
    {
        _hasEnteredLiveThisSession = true;

        if (readyStateView != null) readyStateView.SetActive(false);
        if (liveARView != null) liveARView.SetActive(true);

        SetNavVisible(true);
        RefreshNearbyCount();
    }

    /// <summary>
    /// Entering before calibration is trustworthy. Same as EnterLive, plus a
    /// non-blocking banner so a misplaced marker reads as expected rather than broken.
    /// </summary>
    public void EnterLiveUnverified()
    {
        EnterLive();
        ShowWarning();
    }

    /// <summary>
    /// Goes through the normal ShowPanel path, so the discovery count increments
    /// exactly as it would from an AR marker or a search result.
    /// </summary>
    public void EnterLiveWithBuilding(BuildingData building)
    {
        EnterLive();
        ShowBuilding(building);
    }

    void SetNavVisible(bool visible)
    {
        if (bottomNav != null)
            bottomNav.SetActive(visible);
    }

    void HandleResumed(float secondsBackgrounded)
    {
        if (secondsBackgrounded <= backgroundResetSeconds)
            return;

        AppLifecycleWatcher.ConsumeBackgroundDuration();
        EnterReady();
    }

    // ---------------------------------------------------------------- warning

    void ShowWarning()
    {
        if (warningBanner == null)
            return;

        warningBanner.SetActive(true);

        if (warningLabel != null)
            warningLabel.text = AppStrings.Get("live.warning");
    }

    void HideWarning()
    {
        if (warningBanner != null)
            warningBanner.SetActive(false);
    }

    void HandleStatusChanged(VpsStatus status)
    {
        // Once calibration is genuinely good the warning has nothing left to warn
        // about, so it retires itself.
        if (status == VpsStatus.Good)
            HideWarning();
    }

    // -------------------------------------------------------------- overlays

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

    /// <summary>Opens the info sheet for a building chosen from a list.</summary>
    public void ShowBuilding(BuildingData building)
    {
        CloseNearby();

        if (infoPanel != null)
            infoPanel.ShowPanel(building);
    }

    void RefreshLabels()
    {
        RefreshNearbyCount();

        if (warningLabel != null)
            warningLabel.text = AppStrings.Get("live.warning");
    }

    void RefreshNearbyCount()
    {
        if (nearbyCountText == null || nearbyPanel == null)
            return;

        nearbyCountText.text = string.Format(
            AppStrings.Get("camera.nearbyCount"), nearbyPanel.NearbyCount);
    }
}
