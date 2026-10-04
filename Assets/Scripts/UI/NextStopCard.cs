using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The one card resting above the tab bar, on the map and over the camera: the
/// next stop, how far it is, and the one thing to do about it.
///
/// It replaced three things at once - the previous / here / next strip, the
/// guidance banner, and the Browse and Team row under them - because Daylight
/// v2 found the reader had to read all three to answer one question. The card
/// answers it and changes with the answer:
///
///   near the stop    You're here · 6 m          Open the stop   [camera]
///   far from it      ➤ 180 m · 2 min walk       Take me there   [ i ]
///   being guided     Follow the blue line       Stop navigation [ i ]
///   all done         Trail complete             All stops
///
/// Which stop is next follows the trail: the creator's order for a guided or
/// scored trail, the nearest unopened one for a freefall trail. Swipe the card
/// up, or press All stops, for every stop.
/// </summary>
public class NextStopCard : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("References")]
    public UserLocationService locationService;
    public MapTabController mapTab;
    public TabNavigationController tabs;

    [Tooltip("Opens the sheet where there is no map to focus - over the camera.")]
    public InfoPanelController infoPanel;

    [Tooltip("Over the camera the card is dark, and Open needs no camera button beside it.")]
    public bool onCamera;

    [Tooltip("Every stop, as a sheet: what All stops and the swipe up open.")]
    public StopListView stopList;

    [Header("Head")]
    public TextMeshProUGUI headLabel;
    public Button listButton;
    public TextMeshProUGUI listLabel;

    [Header("Stop")]
    public Image badge;
    public TextMeshProUGUI numberLabel;
    public Image badgeTick;
    public TextMeshProUGUI titleLabel;
    public Image statusDot;
    public BearingArrow statusArrow;
    public TextMeshProUGUI statusLabel;

    [Header("Actions")]
    public Button primaryButton;
    public TextMeshProUGUI primaryLabel;
    public GameObject primaryIcon;
    public Button secondaryButton;
    public TextMeshProUGUI secondaryLabel;
    public Button iconButton;
    public Image iconImage;
    public Sprite cameraSprite;
    public Sprite infoSprite;

    BuildingData _next;
    BuildingData _shown;
    int _nextIndex;
    int _routeCount;
    bool _complete;
    float _timer;
    float _dragY;

    enum Action { None, Open, Guide, StopGuide, Camera, Info, List, Trails }
    Action _primary, _secondary, _icon;

    /// <summary>The stop the card is pointing at, or null.</summary>
    public BuildingData Next { get { return _next; } }

    /// <summary>The card's next stop, for the map to draw it as the next pin.</summary>
    public static BuildingData Current { get; private set; }

    public static event System.Action OnNextChanged;

    void Awake()
    {
        if (primaryButton != null) primaryButton.onClick.AddListener(() => Run(_primary));
        if (secondaryButton != null) secondaryButton.onClick.AddListener(() => Run(_secondary));
        if (iconButton != null) iconButton.onClick.AddListener(() => Run(_icon));
        if (listButton != null) listButton.onClick.AddListener(() => Run(Action.List));
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        BuildingDatabase.OnBuildingsChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        TrailService.OnTrailsChanged += Refresh;
        MarkerTaskService.OnProgressChanged += Refresh;
        GuidanceService.OnGuidanceChanged += Refresh;
        DiscoveryTracker.OnDiscoveryChanged += Refresh;
        ThemeManager.OnThemeChanged += Refresh;

        Refresh();
    }

    void OnDisable()
    {
        BuildingDatabase.OnBuildingsChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        TrailService.OnTrailsChanged -= Refresh;
        MarkerTaskService.OnProgressChanged -= Refresh;
        GuidanceService.OnGuidanceChanged -= Refresh;
        DiscoveryTracker.OnDiscoveryChanged -= Refresh;
        ThemeManager.OnThemeChanged -= Refresh;

        // Switched away mid-drag: rest where the layout put it.
        if (_settle != null)
        {
            StopCoroutine(_settle);
            _settle = null;
            Vector3 p = transform.localPosition;
            transform.localPosition = new Vector3(p.x, _restY, p.z);
        }
    }

    void Update()
    {
        // Distance is the only part that changes as the reader walks.
        _timer += Time.unscaledDeltaTime;
        if (_timer < 0.5f)
            return;

        _timer = 0f;
        Refresh();
    }

    // ---------------------------------------------------------------- actions

    void Run(Action action)
    {
        switch (action)
        {
            case Action.Open:
            case Action.Info:
                if (_shown == null)
                    break;

                if (mapTab != null && mapTab.isActiveAndEnabled)
                    mapTab.Focus(_shown);
                else if (infoPanel != null)
                    infoPanel.ShowPanel(_shown);
                break;

            case Action.Guide:
                if (_next != null)
                {
                    GuidanceService.Guide(_next);
                    ProximityToast.Say(string.Format(AppStrings.Get("guide.toast"), _next.DisplayName));
                }
                break;

            case Action.StopGuide:
                GuidanceService.Stop();
                break;

            case Action.Camera:
                if (tabs != null)
                    tabs.ShowCamera();
                break;

            case Action.List:
                if (stopList != null)
                    stopList.Open();
                else if (tabs != null)
                    tabs.ShowBrowse();
                break;

            case Action.Trails:
                if (DoorFlow.Instance != null)
                    DoorFlow.Instance.OpenTrails();
                break;
        }
    }

    // Swipe up for every stop. A vertical gesture on purpose: the tab bar under
    // the card is the horizontal one.
    //
    // Measured from where the card rests, which the dock's layout decides. This
    // used to write a height of 0 - the bottom of the dock, not the card's place
    // in it - so any drag, even a downward one, dropped the card into the tab
    // bar and left it there. Down is not a gesture here, as in Daylight: the
    // card gives a little under the finger and comes back.
    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_settle != null)
        {
            StopCoroutine(_settle);
            _settle = null;
        }
        else
        {
            _restY = transform.localPosition.y;
        }

        _dragY = 0f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        _dragY += eventData.delta.y / scale;

        // Up a third of the way the finger goes; down only a few pixels however
        // far it is pulled, never as far as the tab bar.
        float offset = _dragY >= 0f
            ? _dragY * 0.35f
            : -18f * (1f - 1f / (1f + -_dragY / 90f));

        Vector3 p = transform.localPosition;
        transform.localPosition = new Vector3(p.x, _restY + offset, p.z);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _settle = StartCoroutine(Settle());

        if (_dragY > 110f)
            Run(Action.List);
    }

    float _restY;
    Coroutine _settle;

    /// <summary>Back to where it rests, quickly and without a bounce.</summary>
    System.Collections.IEnumerator Settle()
    {
        float from = transform.localPosition.y;
        float t = 0f;

        while (t < 1f)
        {
            t = Mathf.Min(1f, t + Time.unscaledDeltaTime / 0.3f);
            float k = 1f - Mathf.Pow(1f - t, 3f);
            Vector3 p = transform.localPosition;
            transform.localPosition = new Vector3(p.x, Mathf.Lerp(from, _restY, k), p.z);
            yield return null;
        }

        _settle = null;
    }


    // ---------------------------------------------------------------- deciding

    /// <summary>
    /// Finished with a stop. On a trail with points that includes its question,
    /// so "Trail complete" never sends a team home with one unanswered.
    /// </summary>
    public static bool Done(BuildingData stop)
    {
        if (stop == null || !DiscoveryTracker.IsDiscovered(stop))
            return false;

        TrailService trails = TrailService.Instance;
        MarkerTaskService tasks = MarkerTaskService.Instance;

        if (!stop.hasQuestion || trails == null || !trails.SelectedIsGame || tasks == null)
            return true;

        return tasks.Progress(stop.buildingId).IsFinished;
    }

    static bool IsFreefall
    {
        get
        {
            TrailService trails = TrailService.Instance;
            if (trails == null || trails.Selected == null)
                return false;

            return string.Equals(trails.Selected.mode, "freefall",
                                 System.StringComparison.OrdinalIgnoreCase);
        }
    }

    void Choose()
    {
        _next = null;
        _nextIndex = 0;
        _routeCount = 0;
        _complete = false;

        List<BuildingData> all = new List<BuildingData>(BuildingDatabase.AllBuildings);
        all.RemoveAll(b => b == null);

        if (all.Count == 0)
            return;

        List<BuildingData> route = new List<BuildingData>();

        if (!IsFreefall)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].step > 0)
                    route.Add(all[i]);
            }

            route.Sort((a, b) => a.step.CompareTo(b.step));
        }

        if (route.Count > 0)
        {
            _routeCount = route.Count;

            for (int i = 0; i < route.Count; i++)
            {
                if (!Done(route[i]))
                {
                    _next = route[i];
                    _nextIndex = i + 1;
                    return;
                }
            }

            _complete = true;
            return;
        }

        // Freefall, or a guided trail with no order: the nearest one not yet done.
        _routeCount = all.Count;
        double best = double.MaxValue;
        bool located = locationService != null && locationService.HasValidPose;
        bool any = false;

        for (int i = 0; i < all.Count; i++)
        {
            if (Done(all[i]))
                continue;

            any = true;

            if (!located)
            {
                if (_next == null)
                    _next = all[i];
                continue;
            }

            double d = locationService.DistanceMetresTo(all[i]);
            if (d >= 0d && d < best)
            {
                best = d;
                _next = all[i];
            }
        }

        _complete = !any;
    }

    /// <summary>
    /// Close enough that the card says "you're here". The stop's own arrival
    /// radius where it is small, and never more than fifty metres: a hundred-metre
    /// default radius is a rule for crediting a visit, not a sentence anybody
    /// would say to someone a street away.
    /// </summary>
    public static double NearMetres(BuildingData stop)
    {
        double radius = stop.radiusMetres > 0 ? stop.radiusMetres : 30d;
        return System.Math.Max(15d, System.Math.Min(radius, 50d));
    }

    // ---------------------------------------------------------------- drawing

    public void Refresh()
    {
        BuildingData was = _next;
        Choose();

        if (_next != was)
        {
            Current = _next;
            if (OnNextChanged != null)
                OnNextChanged();
        }

        if (listLabel != null)
            listLabel.text = AppStrings.Get("card.all");

        if (_complete)
        {
            DrawComplete();
            return;
        }

        if (_next == null)
        {
            DrawBlank();
            return;
        }

        // A guided stop already found is not one to keep following.
        BuildingData guided = GuidanceService.Target;
        if (guided != null && Done(guided))
            guided = null;

        BuildingData shown = guided != null ? guided : _next;
        _shown = shown;

        bool located = locationService != null && locationService.HasValidPose;
        double metres = located ? locationService.DistanceMetresTo(shown) : -1d;
        bool approximate = locationService != null && locationService.IsApproximate;
        bool near = metres >= 0d && metres <= NearMetres(shown);

        // The head line.
        string head;
        if (guided != null && !near)
            head = AppStrings.Get("card.follow");
        else if (shown == _next && !IsFreefall && _routeCount > 0 && _next.step > 0)
            head = string.Format(AppStrings.Get("card.next"), _nextIndex, _routeCount);
        else
            head = AppStrings.Get("card.nearest");

        Set(headLabel, head);
        DrawBadge(shown, false);
        Set(titleLabel, shown.DisplayName);

        // The status line.
        if (!located || metres < 0d)
        {
            Status(StatusKind.None, AppStrings.Get("map.noLocation"), shown);
        }
        else if (near)
        {
            Status(StatusKind.Here, string.Format(AppStrings.Get("card.here"),
                                                  AppStrings.FormatDistance(metres, approximate)), shown);
        }
        else
        {
            int minutes = Mathf.Max(1, Mathf.RoundToInt((float)(metres / 80d)));
            Status(StatusKind.Far, string.Format(AppStrings.Get("card.walk"),
                                                 AppStrings.FormatDistance(metres, approximate),
                                                 minutes), shown);
        }

        // The actions.
        if (near)
        {
            Actions(Action.Open, "card.open", false, Action.None, null,
                    onCamera ? Action.None : Action.Camera);
        }
        else if (guided != null)
        {
            Actions(Action.None, null, false, Action.StopGuide, "card.stopGuide", Action.Info);
        }
        else
        {
            Actions(Action.Guide, "card.guide", true, Action.None, null, Action.Info);
        }
    }

    void DrawComplete()
    {
        _shown = null;
        Set(headLabel, AppStrings.Get("card.complete"));
        DrawBadge(null, true);
        Set(titleLabel, _routeCount == 1 ? AppStrings.Get("card.allFoundOne")
                                         : string.Format(AppStrings.Get("card.allFound"), _routeCount));
        Status(StatusKind.Good, AppStrings.Get("card.completeBody"), null);

        // "All stops" is already the link in the corner; the way on is another trail.
        Actions(Action.Trails, "card.another", false, Action.None, null, Action.None);
    }

    void DrawBlank()
    {
        _shown = null;
        bool hasTrail = TrailService.Instance != null &&
                        !string.IsNullOrEmpty(TrailService.Instance.SelectedId);

        Set(headLabel, string.Empty);
        DrawBadge(null, false);
        Set(titleLabel, AppStrings.Get(hasTrail ? "strip.loading" : "trails.choose"));
        Status(StatusKind.None, string.Empty, null);
        Actions(hasTrail ? Action.None : Action.Trails, hasTrail ? null : "card.trails", false,
                Action.None, null, Action.None);
    }

    enum StatusKind { None, Here, Far, Good }

    void Status(StatusKind kind, string text, BuildingData target)
    {
        Set(statusLabel, text);

        if (statusLabel != null)
        {
            statusLabel.color = ThemeManager.Get(kind == StatusKind.Here || kind == StatusKind.Good
                ? ThemeRole.StatusGood
                : ThemeRole.TextMuted);

            if (onCamera && kind != StatusKind.Here && kind != StatusKind.Good)
                statusLabel.color = new Color(0.63f, 0.63f, 0.65f);
            else if (onCamera)
                statusLabel.color = new Color(0.188f, 0.82f, 0.345f);
        }

        if (statusDot != null)
        {
            bool dot = kind == StatusKind.Here || kind == StatusKind.Good;
            if (statusDot.gameObject.activeSelf != dot)
                statusDot.gameObject.SetActive(dot);

            statusDot.color = ThemeManager.Get(ThemeRole.GoodDot);
        }

        if (statusArrow != null)
        {
            bool arrow = kind == StatusKind.Far && target != null;
            if (statusArrow.gameObject.activeSelf != arrow)
                statusArrow.gameObject.SetActive(arrow);

            if (arrow)
                statusArrow.SetTarget(target);
        }
    }

    void Actions(Action primary, string primaryKey, bool primaryArrow,
                 Action secondary, string secondaryKey, Action icon)
    {
        _primary = primary;
        _secondary = secondary;
        _icon = icon;

        Show(primaryButton, primary != Action.None);
        if (primary != Action.None)
            Set(primaryLabel, AppStrings.Get(primaryKey));

        if (primaryIcon != null && primaryIcon.activeSelf != primaryArrow)
            primaryIcon.SetActive(primaryArrow);

        Show(secondaryButton, secondary != Action.None);
        if (secondary != Action.None)
            Set(secondaryLabel, AppStrings.Get(secondaryKey));

        Show(iconButton, icon != Action.None);
        if (iconImage != null && icon != Action.None)
        {
            Sprite sprite = icon == Action.Camera ? cameraSprite : infoSprite;
            if (iconImage.sprite != sprite)
                iconImage.sprite = sprite;
        }
    }

    /// <summary>
    /// The stop's own colour with its number on it. The number takes whichever of
    /// white or ink reads better on that colour - creators choose the colour, and
    /// white on the yellow one was barely 2:1.
    /// </summary>
    void DrawBadge(BuildingData stop, bool tick)
    {
        if (badge == null)
            return;

        Color colour = stop != null ? MarkerPalette.Resolve(stop) : ThemeManager.Get(ThemeRole.GoodDot);
        badge.color = colour;

        bool showNumber = !tick && stop != null && stop.step > 0;
        if (numberLabel != null)
        {
            if (numberLabel.gameObject.activeSelf != showNumber)
                numberLabel.gameObject.SetActive(showNumber);

            if (showNumber)
            {
                numberLabel.text = stop.step.ToString();
                numberLabel.color = MarkerPalette.OnColour(colour);
            }
        }

        if (badgeTick != null)
        {
            bool show = tick || (stop != null && stop.step <= 0);
            if (badgeTick.gameObject.activeSelf != show)
                badgeTick.gameObject.SetActive(show);

            badgeTick.color = MarkerPalette.OnColour(colour);

            // A stop with no number gets a plain dot rather than a tick: the tick
            // means done, and this one is not.
            badgeTick.enabled = tick;
        }
    }

    static void Set(TextMeshProUGUI label, string text)
    {
        if (label != null && label.text != text)
            label.text = text;
    }

    static void Show(Button button, bool on)
    {
        if (button != null && button.gameObject.activeSelf != on)
            button.gameObject.SetActive(on);
    }
}
