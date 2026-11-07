using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Every stop on the trail, in its order, as one inset list: the number in the
/// stop's colour, its name, and where it stands - found, here, how far, next.
///
///   2 of 5 found. You open a stop when you are next to it.
///   ①  Безистен                    ● Found
///   ③  Театър „Невена Коканова“    180 m · next
///   ④  Исторически музей           310 m
///
/// Opened by "All stops" and by swiping the next-stop card up. A row opens its
/// stop; the list closes on the way, because the stop's own sheet is the page
/// that follows.
/// </summary>
public class StopListView : MonoBehaviour
{
    public UserLocationService locationService;
    public InfoPanelController infoPanel;

    public TextMeshProUGUI summaryLabel;
    public RectTransform listParent;

    [Tooltip("A row, built once and switched off; copies of it are the list.")]
    public RectTransform rowTemplate;

    public Button backButton;
    public Button closeButton;

    readonly List<RectTransform> _rows = new List<RectTransform>();
    float _timer;

    void Awake()
    {
        if (backButton != null)
            backButton.onClick.AddListener(Close);

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (rowTemplate != null)
            rowTemplate.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        AppStrings.OnLanguageChanged += Draw;
        ThemeManager.OnThemeChanged += Draw;
        BuildingDatabase.OnBuildingsChanged += Draw;
        DiscoveryTracker.OnDiscoveryChanged += Draw;
        HiddenMarkers.OnChanged += Draw;
        Draw();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Draw;
        ThemeManager.OnThemeChanged -= Draw;
        BuildingDatabase.OnBuildingsChanged -= Draw;
        DiscoveryTracker.OnDiscoveryChanged -= Draw;
        HiddenMarkers.OnChanged -= Draw;
    }

    public void Open()
    {
        // Over a stop's sheet it is the next page, so it slides in from the
        // side with a Back. As the first sheet up - from the card, the radar -
        // it rises from the bottom, the way a sheet does.
        bool overStop = infoPanel != null && infoPanel.Current != null;

        if (overStop || sheet == null)
        {
            StopMoving();
            SetPush(true);
            gameObject.SetActive(true);
            SetOffset(0f);
            PageBack.Show(backButton, overStop);
            Draw();
            return;
        }

        BeginPull(SheetHeight());
        EndPull(0f, true);
    }

    public void Close()
    {
        StopMoving();
        _pulling = false;
        gameObject.SetActive(false);
        SetOffset(0f);
    }

    // ------------------------------------------------------- pulled up by hand
    //
    // The next-stop card hands its upward drag here: the list comes up under
    // the finger from the card's top edge, and on letting go springs open or
    // sinks back into the card. One spring for all of it, nearly critically
    // damped, so it lands with the faintest give and never bounces off the
    // bottom of the screen.

    [Header("Pulled up from the card")]
    [Tooltip("The sheet that moves; the page's dim (on the panel) follows it.")]
    public RectTransform sheet;
    public Image scrim;
    public float scrimAlpha = 0.3f;

    const float Stiffness = 320f;
    const float Damping = 33f;

    bool _pulling;
    bool _haveRest;
    float _restY;
    float _offset;
    float _start;
    float _velocity;
    float _lastTime;
    Coroutine _move;

    /// <summary>Whether a pull is under way (from the card).</summary>
    public bool Pulling { get { return _pulling; } }

    /// <summary>
    /// Shows the list sunk <paramref name="startOffset"/> reference pixels below
    /// where it rests, ready to follow the finger.
    /// </summary>
    public void BeginPull(float startOffset)
    {
        StopMoving();
        SetPush(false);
        gameObject.SetActive(true);
        PageBack.Show(backButton, false);
        Draw();

        _pulling = true;
        _start = Mathf.Max(1f, startOffset);
        _velocity = 0f;
        _lastTime = Time.unscaledTime;
        SetOffset(_start);
    }

    /// <summary>Moves it with the finger, up for a positive delta.</summary>
    public void Pull(float deltaUp)
    {
        if (!_pulling)
            return;

        float now = Time.unscaledTime;
        float v = -deltaUp / Mathf.Max(0.001f, now - _lastTime);
        _velocity = Mathf.Lerp(_velocity, v, 0.45f);
        _lastTime = now;

        float next = _offset - deltaUp;

        // Past the top it gives a little and stops, rather than coming away
        // from the bottom of the screen.
        if (next < 0f)
            next = -24f * (1f - 1f / (1f + -next / 120f));

        SetOffset(Mathf.Min(next, _start + 60f));
    }

    /// <summary>Let go: open when it is most of the way or flicked up, else back into the card.</summary>
    public void EndPull(float velocityUp, bool forceOpen = false)
    {
        if (!_pulling)
            return;

        _pulling = false;

        float v = forceOpen ? 0f : (Mathf.Abs(velocityUp) > 1f ? -velocityUp : _velocity);
        bool open = forceOpen || v < -900f || (v < 600f && _offset < _start * 0.7f);

        if (open)
            Haptics.Tick();

        _move = StartCoroutine(SpringTo(open ? 0f : _start, v, !open));
    }

    /// <summary>How tall the sheet is: rising from below the screen means from that far down.</summary>
    float SheetHeight()
    {
        return sheet != null && sheet.rect.height > 1f ? sheet.rect.height + 40f : 2000f;
    }

    /// <summary>How far below its resting place a point in world space is, in the sheet's units.</summary>
    public float OffsetTo(Vector3 worldTop)
    {
        if (sheet == null || sheet.parent == null)
            return SheetHeight();

        RememberRest();
        Vector3[] corners = new Vector3[4];
        sheet.GetWorldCorners(corners);
        float top = sheet.parent.InverseTransformPoint(corners[1]).y - (sheet.anchoredPosition.y - _restY);
        float card = sheet.parent.InverseTransformPoint(worldTop).y;
        return Mathf.Clamp(top - card, 1f, SheetHeight());
    }

    System.Collections.IEnumerator SpringTo(float target, float v, bool closeAtEnd)
    {
        float x = _offset;

        while (true)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            float a = -Stiffness * (x - target) - Damping * v;
            v += a * dt;
            x += v * dt;
            SetOffset(Mathf.Max(x, -8f));

            if (Mathf.Abs(x - target) < 0.75f && Mathf.Abs(v) < 8f)
                break;

            yield return null;
        }

        SetOffset(target);
        _move = null;

        if (closeAtEnd)
            Close();
    }

    void StopMoving()
    {
        if (_move != null)
        {
            StopCoroutine(_move);
            _move = null;
        }
    }

    void RememberRest()
    {
        if (_haveRest || sheet == null)
            return;

        _restY = sheet.anchoredPosition.y;
        _haveRest = true;
    }

    void SetOffset(float offset)
    {
        _offset = offset;

        if (sheet != null)
        {
            RememberRest();
            sheet.anchoredPosition = new Vector2(sheet.anchoredPosition.x, _restY - offset);
        }

        if (scrim != null)
        {
            float k = _start > 1f ? Mathf.Clamp01(1f - offset / _start) : 1f;
            Color c = scrim.color;
            c.a = scrimAlpha * k;
            scrim.color = c;
        }
    }

    void SetPush(bool on)
    {
        if (sheet == null)
            return;

        PagePush push = sheet.GetComponent<PagePush>();
        if (push != null)
        {
            push.enabled = on;
            if (!on)
                sheet.anchoredPosition = new Vector2(0f, sheet.anchoredPosition.y);
        }
    }

    void Update()
    {
        // Distances change as the reader walks.
        _timer += Time.unscaledDeltaTime;
        if (_timer < 1f)
            return;

        _timer = 0f;
        Draw();
    }

    static List<BuildingData> Ordered()
    {
        List<BuildingData> all = new List<BuildingData>(BuildingDatabase.AllBuildings);
        all.RemoveAll(b => b == null);
        all.Sort((a, b) =>
        {
            int sa = a.step > 0 ? a.step : int.MaxValue;
            int sb = b.step > 0 ? b.step : int.MaxValue;
            return sa != sb ? sa.CompareTo(sb) : string.CompareOrdinal(a.DisplayName, b.DisplayName);
        });
        return all;
    }

    void Draw()
    {
        if (listParent == null || rowTemplate == null)
            return;

        List<BuildingData> stops = Ordered();
        int found = 0;
        for (int i = 0; i < stops.Count; i++)
        {
            if (NextStopCard.Done(stops[i]))
                found++;
        }

        if (summaryLabel != null)
            summaryLabel.text = string.Format(AppStrings.Get("list.summary"), found, stops.Count);

        while (_rows.Count < stops.Count)
        {
            RectTransform row = Instantiate(rowTemplate, listParent);
            row.gameObject.SetActive(true);
            _rows.Add(row);
        }

        for (int i = 0; i < _rows.Count; i++)
        {
            bool used = i < stops.Count;
            if (_rows[i].gameObject.activeSelf != used)
                _rows[i].gameObject.SetActive(used);

            if (used)
                DrawRow(_rows[i], stops[i], i == 0);
        }
    }

    void DrawRow(RectTransform row, BuildingData stop, bool first)
    {
        bool done = NextStopCard.Done(stop);
        bool next = stop == NextStopCard.Current;
        Color colour = MarkerPalette.Resolve(stop);

        double metres = locationService != null && locationService.HasValidPose
            ? locationService.DistanceMetresTo(stop)
            : -1d;
        bool near = metres >= 0d && metres <= NextStopCard.NearMetres(stop);
        bool approximate = locationService != null && locationService.IsApproximate;

        Transform rule = row.Find("Rule");
        if (rule != null)
            rule.gameObject.SetActive(!first);

        // The number: filled in its colour once found or next, a ring of it before.
        Image disc = Find<Image>(row, "Line/Badge");
        Image hole = Find<Image>(row, "Line/Badge/Hole");
        TextMeshProUGUI number = Find<TextMeshProUGUI>(row, "Line/Badge/Number");
        bool hollow = !done && !next;

        if (disc != null)
            disc.color = colour;

        if (hole != null)
        {
            hole.gameObject.SetActive(hollow);
            hole.color = ThemeManager.Get(ThemeRole.SurfaceAlt);
        }

        if (number != null)
        {
            number.text = stop.step > 0 ? stop.step.ToString() : "•";
            number.color = hollow ? ThemeManager.Get(ThemeRole.TextPrimary) : MarkerPalette.OnColour(colour);
        }

        TextMeshProUGUI name = Find<TextMeshProUGUI>(row, "Line/Text/Name");
        if (name != null)
            name.text = stop.DisplayName;

        string status;
        bool good = done;

        if (done)
            status = AppStrings.Get("sheet.found");
        else if (near)
            status = AppStrings.Get("list.here") + (next ? " · " + AppStrings.Get("list.next") : string.Empty);
        else if (metres >= 0d)
            status = AppStrings.FormatDistance(metres, approximate) + (next ? " · " + AppStrings.Get("list.next") : string.Empty);
        else
            status = next ? AppStrings.Get("list.nextCap") : string.Empty;

        TextMeshProUGUI line = Find<TextMeshProUGUI>(row, "Line/Text/Status/Label");
        if (line != null)
        {
            line.text = status;
            line.color = ThemeManager.Get(good ? ThemeRole.StatusGood : ThemeRole.TextMuted);
        }

        Image dot = Find<Image>(row, "Line/Text/Status/Dot");
        if (dot != null)
        {
            dot.gameObject.SetActive(good);
            dot.color = ThemeManager.Get(ThemeRole.GoodDot);
        }

        TextMeshProUGUI hidden = Find<TextMeshProUGUI>(row, "Line/Hidden");
        if (hidden != null)
        {
            bool off = HiddenMarkers.IsHidden(stop);
            hidden.gameObject.SetActive(off);
            hidden.text = AppStrings.Get("list.hidden");
        }

        Button button = row.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            BuildingData target = stop;
            button.onClick.AddListener(() =>
            {
                Close();
                if (infoPanel != null)
                    infoPanel.ShowPanel(target);
            });
        }
    }

    static T Find<T>(Transform root, string path) where T : Component
    {
        Transform found = root.Find(path);
        return found != null ? found.GetComponent<T>() : null;
    }
}
