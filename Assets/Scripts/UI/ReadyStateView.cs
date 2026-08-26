using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Camera tab's waiting room. It covers the camera feed while ARCore localizes,
/// and spends that time telling the reader what is actually around them.
///
/// It is not a splash screen: it holds nobody up. "Start anyway" is live at every
/// calibration state, and the whole view is skipped entirely on later visits to the
/// tab within a session.
/// </summary>
public class ReadyStateView : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;
    public BuildingRowView rowPrefab;

    [Header("Greeting")]
    public TextMeshProUGUI greetingText;
    public TextMeshProUGUI headlineText;

    [Header("Closest card")]
    public GameObject closestCard;
    public TextMeshProUGUI closestLabel;
    public RectTransform closestParent;

    [Header("Status card")]
    public Image statusDot;
    public TextMeshProUGUI statusTitle;
    public TextMeshProUGUI statusBody;

    [Header("Buttons")]
    public Button startButton;
    public Image startButtonBackground;
    public TextMeshProUGUI startLabel;
    public Button startAnywayButton;
    public TextMeshProUGUI startAnywayLabel;
    public TextMeshProUGUI captionText;

    [Header("Behaviour")]
    [Tooltip("Buildings further than this are not counted as nearby. Matches the " +
             "Nearby panel's radius.")]
    public double radiusMetres = 500d;

    [Tooltip("How many buildings the Closest card lists.")]
    public int closestCount = 3;

    /// <summary>Wired by CameraTabController.</summary>
    public Action onStart;
    public Action onStartAnyway;
    public Action<BuildingData> onBuildingSelected;

    readonly List<GameObject> _rows = new List<GameObject>();
    string _rowSignature = string.Empty;

    void Awake()
    {
        if (startButton != null)
            startButton.onClick.AddListener(HandleStart);

        if (startAnywayButton != null)
            startAnywayButton.onClick.AddListener(HandleStartAnyway);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        // Driven by the location service's own once-a-second poll, and only while
        // this view is actually on screen.
        if (locationService != null)
        {
            locationService.OnPoseUpdated += Refresh;
            locationService.OnStatusChanged += HandleStatusChanged;
        }

        AppStrings.OnLanguageChanged += Refresh;
        ThemeManager.OnThemeChanged += Refresh;
        BuildingDatabase.OnBuildingsChanged += Refresh;

        _rowSignature = string.Empty;
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
        ThemeManager.OnThemeChanged -= Refresh;
        BuildingDatabase.OnBuildingsChanged -= Refresh;
    }

    void HandleStatusChanged(VpsStatus status)
    {
        Refresh();
    }

    void HandleStart()
    {
        if (onStart != null)
            onStart();
    }

    void HandleStartAnyway()
    {
        if (onStartAnyway != null)
            onStartAnyway();
    }

    public void Refresh()
    {
        RefreshGreeting();
        RefreshClosest();
        RefreshStatus();
    }

    // --------------------------------------------------------------- greeting

    void RefreshGreeting()
    {
        if (greetingText != null)
            greetingText.text = AppStrings.Get(GreetingKey());

        if (headlineText == null)
            return;

        bool hasPose = locationService != null && locationService.HasValidPose;

        headlineText.text = hasPose
            ? string.Format(AppStrings.Get("ready.countHeadline"), NearbyBuildings().Count)
            : AppStrings.Get("ready.finding");
    }

    static string GreetingKey()
    {
        int hour = DateTime.Now.Hour;

        if (hour < 12) return "ready.morning";
        if (hour < 18) return "ready.afternoon";
        return "ready.evening";
    }

    List<BuildingData> NearbyBuildings()
    {
        if (locationService == null || !locationService.HasValidPose)
            return new List<BuildingData>();

        return locationService.BuildingsWithin(radiusMetres);
    }

    // ---------------------------------------------------------------- closest

    void RefreshClosest()
    {
        List<BuildingData> nearby = NearbyBuildings();
        List<BuildingData> closest = nearby.Take(closestCount).ToList();

        if (closestLabel != null)
            closestLabel.text = AppStrings.Get("ready.closest");

        // With nothing in range the card is hidden rather than left empty.
        if (closestCard != null)
            closestCard.SetActive(closest.Count > 0);

        // Rebuilding three rows every second churns garbage for no visible change,
        // so only rebuild when the set itself differs.
        string signature = string.Join("|", closest.Select(b => b.buildingId));
        if (signature == _rowSignature)
        {
            RefreshRowDistances(closest);
            return;
        }

        _rowSignature = signature;
        ClearRows();

        foreach (BuildingData building in closest)
        {
            BuildingData captured = building;
            BuildingRowView row = Instantiate(rowPrefab, closestParent);

            // Name and distance only - the Closest card has no room for a subtitle.
            if (row.subtitleText != null)
                row.subtitleText.gameObject.SetActive(false);

            row.Bind(captured, locationService.DistanceMetresTo(captured),
                     () => Select(captured));

            _rows.Add(row.gameObject);
        }
    }

    void RefreshRowDistances(List<BuildingData> closest)
    {
        for (int i = 0; i < _rows.Count && i < closest.Count; i++)
        {
            if (_rows[i] == null)
                continue;

            BuildingRowView row = _rows[i].GetComponent<BuildingRowView>();
            if (row != null && row.distanceText != null)
                row.distanceText.text = AppStrings.FormatDistance(
                    locationService.DistanceMetresTo(closest[i]), locationService.IsApproximate);
        }
    }

    void Select(BuildingData building)
    {
        if (onBuildingSelected != null)
            onBuildingSelected(building);
    }

    void ClearRows()
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] == null)
                continue;

            _rows[i].transform.SetParent(null, false);
            Destroy(_rows[i]);
        }

        _rows.Clear();
    }

    // ----------------------------------------------------------------- status

    void RefreshStatus()
    {
        VpsStatus status = locationService != null ? locationService.Status : VpsStatus.Unavailable;

        if (statusDot != null)
            statusDot.color = ThemeManager.Get(RoleFor(status));

        string key = StatusKey(status);

        if (statusTitle != null)
            statusTitle.text = AppStrings.Get(key + ".title");

        if (statusBody != null)
            statusBody.text = BuildStatusBody(status, key);

        RefreshButtons(status);
    }

    /// <summary>
    /// Picks the copy. The "no coverage here" case is separated out because
    /// otherwise this screen tells someone to point at facades forever, in a place
    /// where doing so can never help.
    /// </summary>
    string StatusKey(VpsStatus status)
    {
        if (status == VpsStatus.Unavailable &&
            locationService != null &&
            locationService.CoverageHere == Google.XR.ARCoreExtensions.VpsAvailability.Unavailable)
        {
            return "ready.status.novps";
        }

        return KeyFor(status);
    }

    string BuildStatusBody(VpsStatus status, string key)
    {
        string body = AppStrings.Get(key + ".body");

        // Only the Good state quotes a number, and it is always read live rather
        // than cached, so it stays honest after the session re-converges.
        if (status == VpsStatus.Good && locationService != null && locationService.HasValidPose)
        {
            string accuracy = string.Format(AppStrings.Get("vps.accuracy"),
                                            locationService.HorizontalAccuracy.ToString("0.0"));
            return string.Format(body, accuracy);
        }

        return body;
    }

    void RefreshButtons(VpsStatus status)
    {
        bool ready = status == VpsStatus.Fair || status == VpsStatus.Good;

        if (startButton != null)
            startButton.interactable = ready;

        if (startButtonBackground != null)
        {
            startButtonBackground.color = ready
                ? ThemeManager.Get(ThemeRole.Accent)
                : ThemeManager.Get(ThemeRole.SurfaceAlt);
        }

        if (startLabel != null)
        {
            startLabel.text = AppStrings.Get(ready ? "ready.start" : "ready.preparing");
            startLabel.color = ThemeManager.Get(ready ? ThemeRole.OnAccent : ThemeRole.TextMuted);
        }

        if (startAnywayLabel != null)
            startAnywayLabel.text = AppStrings.Get("ready.startAnyway");

        if (captionText != null)
            captionText.text = AppStrings.Get("ready.cameraOff");
    }

    static ThemeRole RoleFor(VpsStatus status)
    {
        switch (status)
        {
            case VpsStatus.Good: return ThemeRole.StatusGood;
            case VpsStatus.Fair: return ThemeRole.StatusFair;
            case VpsStatus.Poor: return ThemeRole.StatusPoor;
            default: return ThemeRole.StatusNone;
        }
    }

    static string KeyFor(VpsStatus status)
    {
        switch (status)
        {
            case VpsStatus.Good: return "ready.status.good";
            case VpsStatus.Fair: return "ready.status.fair";
            case VpsStatus.Poor: return "ready.status.poor";
            default: return "ready.status.none";
        }
    }
}
