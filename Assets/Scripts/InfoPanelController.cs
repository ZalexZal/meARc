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

    [Tooltip("Capture mode: moving a stop on your own trail to where it really is. " +
             "Only on screen for the trail's owner and editors.")]
    public Button moveButton;
    public TextMeshProUGUI moveLabel;
    public CaptureMode capture;

    [Header("Daylight v2")]
    [Tooltip("The stop's number on its colour.")]
    public TextMeshProUGUI numberLabel;

    [Tooltip("Green beside \"You're here\" and \"Found\".")]
    public Image statusDot;

    [Tooltip("The one thing to do next: answer, go there, the next stop.")]
    public Button footerPrimary;
    public TextMeshProUGUI footerPrimaryLabel;
    public GameObject footerPrimaryIcon;

    [Tooltip("The quieter version of the same slot: stop navigating, close.")]
    public Button footerSecondary;
    public TextMeshProUGUI footerSecondaryLabel;

    [Tooltip("Learn more, hide, move and report, behind the ⋯.")]
    public SheetMenu menu;
    public SheetDetents detents;

    [Tooltip("Every stop on the trail, as a sheet. Built with this one, opened by the cards.")]
    public StopListView stopList;

    enum Next { None, Answer, Guide, StopGuide, NextStop, Close }
    Next _footer;
    bool _reveal;

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

        if (footerPrimary != null)
            footerPrimary.onClick.AddListener(RunFooter);

        if (footerSecondary != null)
            footerSecondary.onClick.AddListener(RunFooter);

        if (detents != null)
            detents.onDismissed.AddListener(ClosePanel);

        // "Back to the next stop" on an answered question closes the sheet: the
        // next stop is on the card under it, and the map is the way there.
        if (pieces != null && pieces.question != null)
            pieces.question.onLeave = ClosePanel;

        if (guideButton != null)
            guideButton.onClick.AddListener(ToggleGuidance);

        if (hideButton != null)
            hideButton.onClick.AddListener(HideThisMarker);

        if (moveButton != null)
            moveButton.onClick.AddListener(MoveThisMarker);

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
        MarkerTaskService.OnProgressChanged += RefreshFooter;
        DoorFlow.OnDoorChanged += CloseUnderDoor;
        ReportSheetView.OnBlocked += CloseAfterBlock;

        RefreshLabels();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= RefreshLabels;
        GuidanceService.OnGuidanceChanged -= RefreshLabels;
        MarkerTaskService.OnProgressChanged -= RefreshFooter;
        DoorFlow.OnDoorChanged -= CloseUnderDoor;
        ReportSheetView.OnBlocked -= CloseAfterBlock;
    }

    /// <summary>
    /// Opens capture mode for this stop. The sheet closes: the job is done
    /// looking through the camera at the street, not at this card.
    /// </summary>
    public void MoveThisMarker()
    {
        BuildingData data = currentBuilding;

        if (data == null || capture == null)
            return;

        ClosePanel();
        capture.Begin(data);
    }

    /// <summary>A stop made by somebody just blocked is not one to leave on screen.</summary>
    void CloseAfterBlock()
    {
        if (currentBuilding != null)
            ClosePanel();
    }

    /// <summary>Raised when a stop's sheet opens, with the stop.</summary>
    public static event Action<BuildingData> OnOpened;

    public static event Action OnClosed;

    /// <summary>One sheet at a time: a door going up closes the stop under it.</summary>
    void CloseUnderDoor(DoorFlow.Door door)
    {
        if (door != DoorFlow.Door.None && currentBuilding != null)
            ClosePanel();
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
        RefreshFooter();
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
        {
            GuidanceService.Stop();
        }
        else
        {
            GuidanceService.Guide(currentBuilding);
            ProximityToast.Say(string.Format(AppStrings.Get("guide.toast"), currentBuilding.DisplayName));
        }

        ClosePanel();
    }

    public void HideThisMarker()
    {
        if (currentBuilding == null)
            return;

        BuildingData stop = currentBuilding;
        HiddenMarkers.Hide(stop);
        ClosePanel();

        // Said, and undoable for a moment: a stop hidden by a slip of the thumb
        // should not need Profile to bring it back.
        ProximityToast.Say(AppStrings.Get("hide.toast"), AppStrings.Get("hide.undo"),
                           () => HiddenMarkers.Unhide(stop));
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

        if (menu != null)
            menu.Close();

        if (detents != null)
            detents.ToLarge();

        reportSheet.Open(currentBuilding.buildingId, currentBuilding.DisplayName);
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
        UserLocationService location = UserLocationService.Instance;

        // A faked position reaches nothing, by either route. The phone says so
        // itself; see UserLocationService.IsMocked.
        if (location != null && location.IsMocked)
            return false;

        if (fromMarker)
            return true;

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
    /// Whether this stop's question and media may be shown at all.
    ///
    /// The same rule as <see cref="MayCredit"/>, because it is the same
    /// concession. Opening a stop from the map is the fallback for somebody
    /// standing in front of one whose AR will not calibrate - not a way to read
    /// the trail from a bench. Tapping a pin used to hand over every question
    /// and every piece of media at any distance, which on a scored trail is the
    /// answers, and which made the AR half of the app optional.
    ///
    /// A stop already found stays readable, so a team can look at the question
    /// again while they work it out, and so somebody who walked a trail last
    /// week can still read what they saw.
    /// </summary>
    static bool MayReveal(BuildingData data, bool fromMarker)
    {
        // The demo asks nobody to walk, so its stops open from wherever you are.
        return DemoTrail.Owns(data) || MayCredit(data, fromMarker) ||
               DiscoveryTracker.IsDiscovered(data);
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

        // Worked out before anything is drawn, because it decides both what the
        // sheet shows and whether the contents are asked for at all.
        bool reveal = MayReveal(data, fromMarker);
        _reveal = reveal;

        // Hiding a practice stop would hide it from every replay, and a report
        // would put a stop that does not exist in the moderators' queue.
        bool demo = DemoTrail.Owns(data);

        // A stop on the reader's own trail - or one they edit - is theirs to put
        // right, not to hide from themselves or to report to somebody else.
        bool editable = !demo && capture != null && CaptureMode.CanEdit(data);

        if (moveButton != null)
            moveButton.gameObject.SetActive(editable);

        if (hideButton != null)
            hideButton.gameObject.SetActive(!demo && !editable);

        if (reportButton != null)
            reportButton.gameObject.SetActive(reportSheet != null && !demo && !editable);

        RenderBuilding(data);

        // After RenderBuilding, because the pieces list reads the marker's task and
        // media and both are language-aware.
        if (pieces != null)
        {
            if (reveal)
                pieces.Bind(data);
            else
                pieces.BindLocked(data);
        }
        RefreshLabels();

        // Being there is what counts, not seeing it.
        //
        // This used to fire for every route into the sheet, and the comment here
        // said so approvingly. On a guided walk that is harmless. On a
        // competitive trail it means a marker can be explored from a bench on the
        // other side of town by tapping its pin on the map, which is the whole
        // game given away.
        if (MayCredit(data, fromMarker) || DemoTrail.Owns(data))
        {
            DiscoveryTracker.MarkDiscovered(data);

            // Found what it was leading to, so the blue line has done its job.
            if (GuidanceService.Target == data)
                GuidanceService.Stop();

            // And to the server, which is what makes it count towards trails
            // walked and badges. The phone's own flag stays for the screens that
            // read it offline; this is the record that survives a new phone.
            if (ProfileService.Instance != null && !DemoTrail.Owns(data))
                ProfileService.Instance.RecordVisit(data.buildingId, fromMarker);
        }

        // Not fetched unless it may be shown. Hiding a question the phone has
        // already downloaded is a curtain rather than a rule, and anybody minded
        // to look would find it in the response. Asking only when the reader is
        // allowed to read is the honest version of the same thing.
        if (reveal && MarkerDetailService.Instance != null)
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

        if (menu != null)
            menu.Close();

        // A stop opens half way: its name, where it is, the first of its words,
        // and the map still showing above it.
        if (detents != null)
        {
            detents.Snap(false);

            // From fully below the screen at the new height, whatever height it
            // was last put away at.
            if (sheet != null && !sheet.IsOpen)
                sheet.SnapClosed();
        }

        RefreshFooter();

        if (OnOpened != null)
            OnOpened(data);

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

        if (numberLabel != null)
        {
            bool numbered = data.step > 0;
            numberLabel.gameObject.SetActive(numbered);
            numberLabel.text = numbered ? data.step.ToString() : string.Empty;
            numberLabel.color = MarkerPalette.OnColour(colour);
        }

        if (bearingArrow != null)
            bearingArrow.SetTarget(data);

        RefreshDistance();
    }

    /// <summary>
    /// The line under the stop's number: found, here, or how far and which way.
    /// </summary>
    void RefreshDistance()
    {
        if (distanceText == null)
            return;

        if (locationService == null)
            locationService = UserLocationService.Instance;

        double metres = locationService != null && currentBuilding != null
            ? locationService.DistanceMetresTo(currentBuilding)
            : -1d;

        bool approximate = locationService != null && locationService.IsApproximate;
        bool found = currentBuilding != null && DiscoveryTracker.IsDiscovered(currentBuilding);
        bool near = currentBuilding != null && metres >= 0d && metres <= NextStopCard.NearMetres(currentBuilding);

        string text;
        if (found)
            text = AppStrings.Get("sheet.found");
        else if (metres < 0d)
            text = AppStrings.Get("map.noLocation");
        else if (near)
            text = string.Format(AppStrings.Get("card.here"), AppStrings.FormatDistance(metres, approximate));
        else
            text = string.Format(AppStrings.Get("sheet.away"), AppStrings.FormatDistance(metres, approximate));

        if (distanceText.text != text)
            distanceText.text = text;

        bool good = found || near;
        distanceText.color = ThemeManager.Get(good ? ThemeRole.StatusGood : ThemeRole.TextMuted);

        if (statusDot != null && statusDot.gameObject.activeSelf != good)
            statusDot.gameObject.SetActive(good);

        if (statusDot != null)
            statusDot.color = ThemeManager.Get(ThemeRole.GoodDot);

        if (bearingArrow != null)
        {
            bool arrow = !good && metres >= 0d;
            if (bearingArrow.gameObject.activeSelf != arrow)
                bearingArrow.gameObject.SetActive(arrow);
        }
    }

    // ------------------------------------------------------------- the footer

    /// <summary>
    /// The one thing to do next, in the one slot at the foot of the sheet.
    ///
    ///   a question you can answer here     Answer the question
    ///   locked after a wrong answer        Locked · 3:12 (dimmed)
    ///   being guided here                  Stop navigation
    ///   not there yet                      ➤ Take me there
    ///   done, with a next stop             Next stop
    ///   anything else                      Close
    /// </summary>
    void RefreshFooter()
    {
        BuildingData data = currentBuilding;
        if (data == null || (footerPrimary == null && footerSecondary == null))
            return;

        MarkerTaskService tasks = MarkerTaskService.Instance;
        MarkerTaskService.MarkerProgress progress = tasks != null
            ? tasks.Progress(data.buildingId)
            : new MarkerTaskService.MarkerProgress();

        double metres = locationService != null ? locationService.DistanceMetresTo(data) : -1d;
        bool near = metres >= 0d && metres <= NextStopCard.NearMetres(data);
        bool found = DiscoveryTracker.IsDiscovered(data);
        bool answerable = _reveal && data.HasTask && !progress.solved && !progress.skipped &&
                          !progress.awaitingReview;

        string label;
        bool primary = true;
        bool arrow = false;
        bool enabled = true;

        if (answerable)
        {
            _footer = Next.Answer;

            if (progress.IsLocked)
            {
                int left = Mathf.CeilToInt(progress.SecondsLeft);
                label = string.Format(AppStrings.Get("sheet.locked"), (left / 60) + ":" + (left % 60).ToString("00"));
                enabled = false;
            }
            else
            {
                label = AppStrings.Get("sheet.answer");
            }
        }
        else if (GuidanceService.Target == data)
        {
            _footer = Next.StopGuide;
            label = AppStrings.Get("card.stopGuide");
            primary = false;
        }
        else if (!found && !near && metres >= 0d)
        {
            _footer = Next.Guide;
            label = AppStrings.Get("card.guide");
            arrow = true;
        }
        else if (found && NextStopCard.Current != null && NextStopCard.Current != data)
        {
            _footer = Next.NextStop;
            label = AppStrings.Get("sheet.nextStop");
        }
        else
        {
            _footer = Next.Close;
            label = AppStrings.Get("sheet.close");
            primary = false;
        }

        if (footerPrimary != null && footerPrimary.gameObject.activeSelf != primary)
            footerPrimary.gameObject.SetActive(primary);

        if (footerSecondary != null && footerSecondary.gameObject.activeSelf != !primary)
            footerSecondary.gameObject.SetActive(!primary);

        TextMeshProUGUI target = primary ? footerPrimaryLabel : footerSecondaryLabel;
        if (target != null && target.text != label)
            target.text = label;

        if (footerPrimaryIcon != null && footerPrimaryIcon.activeSelf != arrow)
            footerPrimaryIcon.SetActive(arrow);

        if (footerPrimary != null && footerPrimary.interactable != enabled)
            footerPrimary.interactable = enabled;
    }

    void RunFooter()
    {
        switch (_footer)
        {
            case Next.Answer:
                if (pieces != null)
                {
                    if (detents != null)
                        detents.ToLarge();

                    pieces.OpenQuestion();
                }
                break;

            case Next.Guide:
            case Next.StopGuide:
                ToggleGuidance();
                break;

            case Next.NextStop:
            case Next.Close:
                ClosePanel();
                break;
        }
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

        RefreshFooter();

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

    /// <summary>Close, from a page over the sheet - which may be open over nothing.</summary>
    public void CloseIfOpen()
    {
        if (currentBuilding != null)
            ClosePanel();
    }

    public void ClosePanel()
    {
        // Before the sheet slides away, so nothing is still playing to an empty
        // screen for the quarter of a second the animation takes.
        if (pieces != null)
            pieces.Close();

        currentBuilding = null;

        if (menu != null)
            menu.Close();

        if (OnClosed != null)
            OnClosed();

        if (scrim != null)
            scrim.SetActive(false);

        if (sheet != null)
            sheet.Close();
        else
            panelRoot.SetActive(false);
    }
}
