using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class InfoPanelController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject panelRoot;
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI metaText;
    public TextMeshProUGUI descriptionText;
    public GameObject learnMoreButton;

    [Header("Dismiss")]
    [Tooltip("Full-screen transparent catcher behind the sheet. Tapping it closes " +
             "the sheet, which is what people expect from a bottom sheet.")]
    public GameObject scrim;
    public Button scrimButton;

    [Header("Optional")]
    [Tooltip("Slides the sheet in and out. Without one the panel just appears.")]
    public SheetAnimator sheet;

    public TextMeshProUGUI learnMoreLabel;

    [Header("Marker identity")]
    [Tooltip("The marker's own colour, so the sheet and the thing on the map or in " +
             "the street are recognisably the same object.")]
    public Image colourDot;

    [Tooltip("Ring behind the dot. Carries the separation when a marker's colour " +
             "happens to be the app's own accent.")]
    public Image colourRing;

    [Header("Distance")]
    public UserLocationService locationService;
    public TextMeshProUGUI distanceText;
    public BearingArrow bearingArrow;

    [Header("Pieces")]
    [Tooltip("The video, audio and question a marker can hold. Null on a build " +
             "without them; the sheet then behaves exactly as it did before.")]
    public MarkerPieceController pieces;

    [Header("Actions")]
    public Button guideButton;
    public TextMeshProUGUI guideLabel;

    [Tooltip("Takes this marker off the map for this reader. Local and immediate - " +
             "it does not wait for anyone to agree.")]
    public Button hideButton;
    public TextMeshProUGUI hideLabel;

    [Tooltip("Tells the people who run the trail. Hide is for one reader, this " +
             "is for everybody, and only one of them needs a human to agree.")]
    public Button reportButton;
    public TextMeshProUGUI reportLabel;

    [Tooltip("The reason chooser. Null on a build without it, and the button " +
             "then stays hidden rather than doing nothing when tapped.")]
    public ReportSheetView reportSheet;

    [Tooltip("The hint ladder. Handed to the question view, which is what opens " +
             "it - the button lives beside the question, not on this sheet.")]
    public HintSheetView hintSheet;

    /// <summary>Raised before the sheet opens, so the Camera tab can close Nearby.</summary>
    public Action onWillOpen;

    private string currentLink;
    private BuildingData currentBuilding;
    private float _distanceTimer;

    /// <summary>The marker this sheet is showing, or null when it is closed.</summary>
    public BuildingData Current { get { return currentBuilding; } }

    void Awake()
    {
        if (scrimButton != null)
            scrimButton.onClick.AddListener(ClosePanel);

        if (guideButton != null)
            guideButton.onClick.AddListener(ToggleGuidance);

        if (hideButton != null)
            hideButton.onClick.AddListener(HideThisMarker);

        if (reportButton != null)
        {
            reportButton.onClick.AddListener(ReportThisMarker);

            // A local marker from the CSV has no server row to report. Better an
            // absent button than one that answers a tap with an error.
            reportButton.gameObject.SetActive(reportSheet != null);
        }

        if (scrim != null)
            scrim.SetActive(false);

        // With an animator the sheet stays enabled and simply sits offscreen, so
        // its own controller keeps running. Without one, fall back to hiding it.
        if (sheet != null)
            sheet.SnapClosed();
        else if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        AppStrings.OnLanguageChanged += RefreshLabels;
        GuidanceService.OnGuidanceChanged += RefreshLabels;

        RefreshLabels();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= RefreshLabels;
        GuidanceService.OnGuidanceChanged -= RefreshLabels;
    }

    void Update()
    {
        if (currentBuilding == null || distanceText == null)
            return;

        _distanceTimer += Time.unscaledDeltaTime;
        if (_distanceTimer < 0.5f)
            return;

        _distanceTimer = 0f;
        RefreshDistance();
    }

    // -------------------------------------------------------------- actions

    /// <summary>
    /// Starts or stops walking to this marker. The sheet closes either way, because
    /// the answer to "guide me there" is the map, not more of this sheet.
    /// </summary>
    public void ToggleGuidance()
    {
        if (currentBuilding == null)
            return;

        if (GuidanceService.Target == currentBuilding)
            GuidanceService.Stop();
        else
            GuidanceService.Guide(currentBuilding);

        ClosePanel();
    }

    public void HideThisMarker()
    {
        if (currentBuilding == null)
            return;

        HiddenMarkers.Hide(currentBuilding);
        ClosePanel();
    }

    /// <summary>
    /// Opens the reason chooser for the stop on screen.
    ///
    /// The sheet stays where it is behind it. Reporting is a thing you do about
    /// what you are looking at, and taking it away first would leave somebody
    /// choosing a reason for a stop they can no longer see the name of.
    /// </summary>
    public void ReportThisMarker()
    {
        if (currentBuilding == null || reportSheet == null)
            return;

        reportSheet.Open(currentBuilding.buildingId);
    }

    /// <summary>
    /// Whether opening this stop counts as having explored it.
    ///
    /// The rule is the AR marker: walk to it, see it standing on the place, tap
    /// it. Nothing else earns the credit.
    ///
    /// The exception below is not a loophole, it is the difference between a
    /// strict rule and an unplayable one. Terrain anchors come from ARCore
    /// Geospatial, so when Earth tracking is not up there is no marker in the
    /// world to tap - and "VPS unavailable" is a state this app has already been
    /// observed in, in the town it was built for. Without a fallback a trail
    /// would simply be uncompletable there. So when the AR path is unavailable,
    /// standing inside the stop's own radius is accepted instead, by GPS.
    ///
    /// Widened by the fix's own accuracy, never narrowed: a 40-metre reading
    /// against a 20-metre radius should not make a stop impossible, whereas a
    /// generous radius costs a few metres of rigour on the one path that is
    /// already the concession.
    /// </summary>
    static bool MayCredit(BuildingData data, bool fromMarker)
    {
        if (fromMarker)
            return true;

        UserLocationService location = UserLocationService.Instance;

        if (location == null || !location.HasValidPose)
            return false;

        // Earth tracking is up, so the marker is out there to be tapped, and
        // tapping it is the rule.
        if (location.HasPrecisePose)
            return false;

        double radius = data.radiusMetres > 0 ? data.radiusMetres : DefaultArrivalMetres;
        radius = System.Math.Max(radius, location.HorizontalAccuracy);

        return location.DistanceMetresTo(data) <= radius;
    }

    /// <summary>
    /// Used when the server sent no radius - a marker placed before 0017, or a
    /// build talking to an older database. The same 100 metres the column
    /// defaults to, so the two agree.
    /// </summary>
    const double DefaultArrivalMetres = 100d;

    /// <summary>
    /// Opens a stop.
    ///
    /// <paramref name="fromMarker"/> is true only when the reader tapped the
    /// marker standing in the world. Everything else - the map pin, the nearby
    /// list, the proximity toast - opens the same sheet and does not count as
    /// having been there. See <see cref="MayCredit"/>.
    /// </summary>
    public void ShowPanel(BuildingData data, bool fromMarker = false)
    {
        if (data == null)
        {
            Debug.LogWarning("InfoPanelController: ShowPanel called with no BuildingData.");
            return;
        }

        if (onWillOpen != null)
            onWillOpen();

        currentBuilding = data;

        RenderBuilding(data);

        // After RenderBuilding, because the pieces list reads the marker's task and
        // media and both are language-aware.
        if (pieces != null)
            pieces.Bind(data);
        RefreshLabels();

        // Being there is what counts, not seeing it.
        //
        // This used to fire for every route into the sheet, and the comment here
        // said so approvingly. On a guided walk that is harmless. On a
        // competitive trail it means a marker can be explored from a bench on the
        // other side of town by tapping its pin on the map, which is the whole
        // game given away.
        if (MayCredit(data, fromMarker))
        {
            DiscoveryTracker.MarkDiscovered(data);

            // And to the server, which is what makes it count towards trails
            // walked and badges. The phone's own flag stays for the screens that
            // read it offline; this is the record that survives a new phone.
            if (ProfileService.Instance != null)
                ProfileService.Instance.RecordVisit(data.buildingId, fromMarker);
        }

        // The contents are fetched either way, because reading a stop from the map
        // is a legitimate thing to do - it just is not the same as walking to it.
        // The sheet is already open by the time this returns; the pieces list
        // fills in underneath.
        if (MarkerDetailService.Instance != null)
        {
            MarkerDetailService.Instance.Fill(data, () =>
            {
                // Guard against a second stop having been opened while this was in
                // the air, which would repaint the sheet with the wrong marker.
                if (currentBuilding != data)
                    return;

                if (pieces != null)
                    pieces.Bind(data);
            });
        }

        if (scrim != null)
            scrim.SetActive(true);

        if (sheet != null)
            sheet.Open();
        else if (panelRoot != null)
            panelRoot.SetActive(true);
    }

    /// <summary>
    /// Every field is read through its language-aware property, so switching
    /// language re-renders the whole sheet rather than only its chrome.
    /// </summary>
    void RenderBuilding(BuildingData data)
    {
        titleText.text = data.DisplayName;
        metaText.text = BuildMetaLine(data);
        descriptionText.text = data.Description;
        currentLink = data.ExternalLink;

        learnMoreButton.SetActive(!string.IsNullOrWhiteSpace(currentLink));

        Color colour = MarkerPalette.Resolve(data);

        if (colourDot != null)
            colourDot.color = colour;

        if (colourRing != null)
            colourRing.color = MarkerPalette.RingFor(colour);

        if (bearingArrow != null)
            bearingArrow.SetTarget(data);

        RefreshDistance();
    }

    void RefreshDistance()
    {
        if (distanceText == null)
            return;

        if (locationService == null)
            locationService = UserLocationService.Instance;

        double metres = locationService != null && currentBuilding != null
            ? locationService.DistanceMetresTo(currentBuilding)
            : -1d;

        if (metres < 0d)
        {
            distanceText.text = AppStrings.Get("map.noLocation");
            return;
        }

        distanceText.text = AppStrings.FormatDistance(
            metres, locationService != null && locationService.IsApproximate);
    }

    // Joins only the fields that are actually filled in, so a building missing an
    // architect doesn't render as "1979  ·    ·  Block".
    string BuildMetaLine(BuildingData data)
    {
        List<string> parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(data.YearBuilt)) parts.Add(data.YearBuilt);
        if (!string.IsNullOrWhiteSpace(data.Architect)) parts.Add(data.Architect);

        string category = data.category != null ? data.category.DisplayName : data.buildingType;
        if (!string.IsNullOrWhiteSpace(category)) parts.Add(category);

        return string.Join("  ·  ", parts);
    }

    void RefreshLabels()
    {
        if (learnMoreLabel != null)
            learnMoreLabel.text = AppStrings.Get("info.learnMore");

        // The one button changes what it says rather than sitting beside a second
        // button that is wrong half the time.
        if (guideLabel != null)
        {
            bool guidingHere = currentBuilding != null &&
                               GuidanceService.Target == currentBuilding;

            guideLabel.text = AppStrings.Get(guidingHere ? "guide.stop" : "guide.start");
        }

        if (hideLabel != null)
            hideLabel.text = AppStrings.Get("marker.hide");

        // Re-render the open sheet in the new language rather than leaving stale
        // text until the reader closes and reopens it.
        if (currentBuilding != null && titleText != null)
            RenderBuilding(currentBuilding);
    }

    public void OpenExternalLink()
    {
        if (string.IsNullOrWhiteSpace(currentLink))
            return;

        Application.OpenURL(NormalizeUrl(currentLink));
    }

    // Android refuses to open a URL with no scheme, so "example.com" silently does
    // nothing. Assume https for any link entered without one.
    static string NormalizeUrl(string url)
    {
        url = url.Trim();

        if (url.Contains("://") || url.StartsWith("mailto:"))
            return url;

        return "https://" + url;
    }

    public void ClosePanel()
    {
        // Before the sheet slides away, so nothing is still playing to an empty
        // screen for the quarter of a second the animation takes.
        if (pieces != null)
            pieces.Close();

        currentBuilding = null;

        if (scrim != null)
            scrim.SetActive(false);

        if (sheet != null)
            sheet.Close();
        else
            panelRoot.SetActive(false);
    }
}
