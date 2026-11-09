using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A trail the reader has not joined yet, looked at before starting.
///
///   (‹)        ③- - -④
///          ①- - -②      ⑤      ●  you
///   ┌───────────────────────────────────┐
///   │ ▣ Туризъм · Ямбол                  │
///   │ Ямбол: сърцето на града            │
///   │ [5 спирки] [~20 мин пеша] [1,4 км] │
///   │ По ред · въпроси с подсказки…      │
///   │ Началото е при Безистена, на 350 м │
///   │ (       Започни маршрута         ) │
///   │ (    Заведи ме до началото       ) │
///   └───────────────────────────────────┘
///
/// The map is a schematic, not tiles: the stops placed by their lat/lng in the
/// box above the sheet, in proportion, with the reader's dot when they are near
/// enough to share the picture. An ordered trail gets numbers and a dashed line
/// in its order; an any-order trail gets plain dots, as the main map draws it.
///
/// Opened by DoorFlow.OpenPreview, which calls Begin while the panel is still
/// switched off; the request goes out from OnEnable, on the trail service, so a
/// preview closed mid-request does not strand it.
/// </summary>
public class TrailPreviewView : MonoBehaviour
{
    [Header("Map")]
    [Tooltip("The schematic's box: the whole panel; the sheet covers its bottom.")]
    public RectTransform mapArea;
    public RectTransform legsParent;
    public RectTransform pinsParent;
    [Tooltip("A numbered dark circle, built once and switched off.")]
    public RectTransform pinTemplate;
    [Tooltip("One dashed leg, built once and switched off.")]
    public RectTransform legTemplate;
    public RectTransform userDot;

    [Tooltip("Room kept clear inside the box: left, right, top (under the back button), bottom (over the sheet).")]
    public Vector4 mapPadding = new Vector4(110f, 110f, 210f, 120f);

    [Header("Sheet")]
    public RectTransform sheet;
    public Image categoryIcon;
    [Tooltip("education, tourism, estate, other - TrailFilter.Categories order.")]
    public Sprite[] categoryIcons;
    public TextMeshProUGUI metaLabel;
    public TextMeshProUGUI nameLabel;

    public GameObject tilesRow;
    public TextMeshProUGUI stopsValue;
    public TextMeshProUGUI stopsLabel;
    public GameObject minutesTile;
    public TextMeshProUGUI minutesValue;
    public GameObject lengthTile;
    public TextMeshProUGUI lengthValue;

    public TextMeshProUGUI howLabel;
    public TextMeshProUGUI startLabel;
    [Tooltip("Loading, or the trail could not be read.")]
    public TextMeshProUGUI statusLabel;

    public Button goButton;
    public Button leadButton;

    string _worldId;
    TrailService.TrailPreview _data;
    bool _sample;            // the preview harness's stand-in, with its own position
    bool _hasSamplePosition;
    double _sampleLat, _sampleLng;
    bool _joining;
    int _request;

    readonly List<RectTransform> _pins = new List<RectTransform>();
    readonly List<RectTransform> _legs = new List<RectTransform>();

    // Worked out in Draw, used by the buttons.
    string _startId;

    public string WorldId { get { return _worldId; } }

    void Awake()
    {
        if (pinTemplate != null)
            pinTemplate.gameObject.SetActive(false);
        if (legTemplate != null)
            legTemplate.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Draw;
        Load();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Draw;
        _joining = false;
    }

    /// <summary>Which trail to show. Called before the panel is revealed.</summary>
    public void Begin(string worldId)
    {
        _worldId = worldId;
        _data = null;
        _sample = false;
        _joining = false;

        if (isActiveAndEnabled)
            Load();
        else
            Draw();
    }

    void Load()
    {
        if (_sample)
        {
            Draw();
            return;
        }

        TrailService service = TrailService.Instance;

        if (service == null || string.IsNullOrEmpty(_worldId))
        {
            Draw();
            return;
        }

        if (_data != null && _data.id == _worldId)
        {
            Draw();
            return;
        }

        int request = ++_request;
        string id = _worldId;
        _data = null;
        Draw();

        service.StartCoroutine(service.Preview(id, result =>
        {
            if (request != _request || id != _worldId)
                return;

            _data = result;
            if (isActiveAndEnabled)
                Draw();
        }));
    }

    /// <summary>The preview harness: stand-in data and a stand-in position.</summary>
    public void UseSample(TrailService.TrailPreview data, bool located, double lat, double lng)
    {
        _sample = true;
        _worldId = data != null ? data.id : null;
        _data = data;
        _hasSamplePosition = located;
        _sampleLat = lat;
        _sampleLng = lng;
        Draw();
    }

    bool Located(out double lat, out double lng)
    {
        if (_sample)
        {
            lat = _sampleLat;
            lng = _sampleLng;
            return _hasSamplePosition;
        }

        UserLocationService location = UserLocationService.Instance;
        bool located = location != null && location.HasValidPose;
        lat = located ? location.Latitude : 0d;
        lng = located ? location.Longitude : 0d;
        return located;
    }

    // ------------------------------------------------------------------ drawing

    void Draw()
    {
        TrailService.TrailPreview data = _data;
        bool ready = data != null && data.ok;

        Set(statusLabel != null ? statusLabel.gameObject : null, !ready);
        if (statusLabel != null)
            statusLabel.text = AppStrings.Get(data == null ? "preview.loading" : "preview.failed");

        Set(metaLabel != null ? metaLabel.transform.parent.gameObject : null, ready);
        Set(nameLabel != null ? nameLabel.gameObject : null, ready);
        Set(tilesRow, ready);

        if (goButton != null) goButton.interactable = ready && !_joining;
        if (leadButton != null) leadButton.interactable = ready && !_joining;

        _startId = null;

        if (!ready)
        {
            Set(howLabel != null ? howLabel.gameObject : null, false);
            Set(startLabel != null ? startLabel.gameObject : null, false);
            Set(leadButton != null ? leadButton.gameObject : null, false);
            PlaceMap(null, null, false, false, 0d, 0d);
            return;
        }

        bool ordered = TrailService.IsOrdered(data.mode);

        // --- Category · City, and the name
        if (metaLabel != null)
            metaLabel.text = TrailService.CategoryAndCity(data.category, data.city);
        if (categoryIcon != null && categoryIcons != null &&
            categoryIcons.Length == TrailFilter.Categories.Length)
            categoryIcon.sprite = categoryIcons[TrailFilter.CategoryIndex(data.category)];
        if (nameLabel != null)
            nameLabel.text = data.name ?? string.Empty;

        // --- the stops, in the order they would be walked
        List<TrailRoute.Stop> points = new List<TrailRoute.Stop>();
        for (int i = 0; i < data.stops.Count; i++)
            points.Add(new TrailRoute.Stop(data.stops[i].lat, data.stops[i].lng, data.stops[i].step));

        bool located = Located(out double lat, out double lng);
        int start = located ? TrailRoute.StartIndex(points, ordered, lat, lng)
                            : TrailRoute.StartIndex(points, ordered);
        List<int> order = TrailRoute.Order(points, ordered, start);

        // --- the three tiles; the two worked out from the stops only when all came back
        int stops = data.stopsKnown ? data.stops.Count : data.stopCount;
        if (stopsValue != null)
            stopsValue.text = stops.ToString();
        if (stopsLabel != null)
            stopsLabel.text = AppStrings.Get(stops == 1 ? "preview.stopsOne" : "preview.stops");

        double metres = data.stopsKnown ? TrailRoute.LengthMetres(points, order) : 0d;
        bool measured = data.stopsKnown && metres > 0d;
        Set(minutesTile, measured);
        Set(lengthTile, measured);

        if (measured)
        {
            if (minutesValue != null)
                minutesValue.text = string.Format(AppStrings.Get("preview.minutes"),
                                                  TrailRoute.WalkingMinutes(metres));
            if (lengthValue != null)
                lengthValue.text = AppStrings.FormatDistance(metres);
        }

        // --- how it works: never for a real-estate trail
        string how = data.category == "estate" ? string.Empty : HowItWorks(data, ordered);
        if (howLabel != null)
            howLabel.text = how;
        Set(howLabel != null ? howLabel.gameObject : null, how.Length > 0);

        // --- where it starts, and how far that is
        bool knowsStart = start >= 0 && (data.stopsKnown || ordered);
        if (knowsStart)
            _startId = data.stops[start].id;

        bool startLine = knowsStart && located;
        if (startLabel != null && startLine)
        {
            TrailService.PreviewStop first = data.stops[start];
            double away = GeoMath.DistanceMetres(lat, lng, first.lat, first.lng);
            startLabel.text = string.Format(AppStrings.Get("preview.start"), first.Title,
                                            AppStrings.FormatDistance(away));
        }
        Set(startLabel != null ? startLabel.gameObject : null, startLine);

        // "Take me to the start" needs a start to take them to.
        Set(leadButton != null ? leadButton.gameObject : null, knowsStart);

        PlaceMap(data.stopsKnown ? points : null, order, ordered, located, lat, lng);
    }

    /// <summary>
    /// "По ред · въпроси с подсказки на всяка спирка. Без отбори и точки." Built
    /// from the mode, and from the stops when they say whether there are questions.
    /// </summary>
    static string HowItWorks(TrailService.TrailPreview data, bool ordered)
    {
        string line = AppStrings.Get(ordered ? "chip.inOrder" : "chip.anyOrder");

        if (data.questions > 0)
        {
            bool every = data.stopsKnown && data.questions >= data.stops.Count;
            line += " · " + AppStrings.Get(every ? "preview.questionsAll" : "preview.questions");
        }

        return line + ". " + AppStrings.Get(TrailService.IsGame(data.mode) ? "preview.teams" : "preview.noTeams");
    }

    // --------------------------------------------------------------------- map

    /// <summary>
    /// Fits the stops (and the reader, when close enough to share the picture)
    /// into the part of the panel the sheet leaves, in proportion.
    /// </summary>
    void PlaceMap(List<TrailRoute.Stop> points, List<int> order, bool ordered,
                  bool located, double lat, double lng)
    {
        int count = points != null ? points.Count : 0;

        if (mapArea == null || pinsParent == null)
            return;

        // The sheet has to be its real height before the box above it is known.
        if (sheet != null)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(sheet);
        }

        Rect area = mapArea.rect;
        float bottom = area.yMin;

        if (sheet != null)
        {
            Vector3[] corners = new Vector3[4];
            sheet.GetWorldCorners(corners);
            bottom = Mathf.Max(bottom, mapArea.InverseTransformPoint(corners[1]).y);
        }

        Rect box = Rect.MinMaxRect(area.xMin + mapPadding.x, bottom + mapPadding.w,
                                   area.xMax - mapPadding.y, area.yMax - mapPadding.z);

        // Metres east and north of the first stop: flat over a trail's few km.
        double lat0 = count > 0 ? points[0].lat : lat;
        double lng0 = count > 0 ? points[0].lng : lng;
        double eastPerDegree = 111320d * System.Math.Cos(lat0 * System.Math.PI / 180d);
        const double NorthPerDegree = 110540d;

        List<Vector2> metres = new List<Vector2>();
        for (int i = 0; i < count; i++)
            metres.Add(new Vector2((float)((points[i].lng - lng0) * eastPerDegree),
                                   (float)((points[i].lat - lat0) * NorthPerDegree)));

        Vector2 you = new Vector2((float)((lng - lng0) * eastPerDegree),
                                  (float)((lat - lat0) * NorthPerDegree));

        // The reader joins the picture only when they would not shrink the trail
        // to a dot: within 2 km of its nearest stop.
        bool showYou = located;
        if (located && count > 0)
        {
            float nearest = float.MaxValue;
            for (int i = 0; i < count; i++)
                nearest = Mathf.Min(nearest, (metres[i] - you).magnitude);
            showYou = nearest < 2000f;
        }

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        for (int i = 0; i < count; i++)
        {
            min = Vector2.Min(min, metres[i]);
            max = Vector2.Max(max, metres[i]);
        }
        if (showYou)
        {
            min = Vector2.Min(min, you);
            max = Vector2.Max(max, you);
        }

        Vector2 span = max - min;
        float scale = 1f;
        if (span.x > 1f || span.y > 1f)
            scale = Mathf.Min(span.x > 1f ? box.width / span.x : float.MaxValue,
                              span.y > 1f ? box.height / span.y : float.MaxValue);
        // Never closer in than about 1 px a metre: two stops 20 m apart must not
        // fill the screen as if they were a kilometre apart.
        scale = Mathf.Min(scale, 2.5f);

        Vector2 centreMetres = (min + max) * 0.5f;
        Vector2 centre = box.center;
        System.Func<Vector2, Vector2> place = m => centre + (m - centreMetres) * scale;

        // --- pins
        while (_pins.Count < count && pinTemplate != null)
        {
            RectTransform pin = Instantiate(pinTemplate, pinsParent);
            pin.name = "Pin";
            _pins.Add(pin);
        }

        for (int i = 0; i < _pins.Count; i++)
        {
            bool on = i < count;
            Set(_pins[i].gameObject, on);
            if (!on)
                continue;

            int stop = order[i];
            _pins[i].anchoredPosition = place(metres[stop]);
            // Unordered stops are smaller and carry no number: there is no order to number.
            _pins[i].localScale = ordered ? Vector3.one : new Vector3(0.6f, 0.6f, 1f);

            TextMeshProUGUI number = _pins[i].GetComponentInChildren<TextMeshProUGUI>(true);
            if (number != null)
                number.text = ordered ? (i + 1).ToString() : string.Empty;
        }

        // --- the dashed line, in order, ordered trails only
        int legs = ordered ? Mathf.Max(0, count - 1) : 0;

        while (_legs.Count < legs && legTemplate != null)
        {
            RectTransform leg = Instantiate(legTemplate, legsParent != null ? legsParent : pinsParent);
            leg.name = "Leg";
            _legs.Add(leg);
        }

        for (int i = 0; i < _legs.Count; i++)
        {
            bool on = i < legs;
            Set(_legs[i].gameObject, on);
            if (!on)
                continue;

            Vector2 from = place(metres[order[i]]);
            Vector2 to = place(metres[order[i + 1]]);
            Vector2 delta = to - from;

            _legs[i].anchoredPosition = from + delta * 0.5f;
            _legs[i].sizeDelta = new Vector2(delta.magnitude, _legs[i].sizeDelta.y);
            _legs[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        // --- you
        if (userDot != null)
        {
            Set(userDot.gameObject, showYou);
            if (showYou)
                userDot.anchoredPosition = count > 0 ? place(you) : centre;
            userDot.SetAsLastSibling();
        }
    }

    // ----------------------------------------------------------------- actions

    /// <summary>"Започни маршрута": joins, as tapping a trail always did.</summary>
    public void StartTrail()
    {
        Join(false);
    }

    /// <summary>"Заведи ме до началото": joins, then guides to the start.</summary>
    public void LeadToStart()
    {
        Join(true);
    }

    /// <summary>The round back button, and Android back.</summary>
    public void Back()
    {
        Haptics.Tick();
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.ClosePreview();
    }

    void Join(bool lead)
    {
        TrailService service = TrailService.Instance;

        if (service == null || _joining || string.IsNullOrEmpty(_worldId) || _data == null || !_data.ok)
            return;

        _joining = true;
        Haptics.Tick();
        Draw();

        string id = _worldId;
        string target = lead ? _startId : null;

        service.StartCoroutine(service.JoinListed(id, ok =>
        {
            _joining = false;

            if (!ok)
            {
                // Said over the preview, which stays: the reader can try again.
                ProximityToast.Say(AppStrings.Get("near.failed"));
                if (isActiveAndEnabled)
                    Draw();
                return;
            }

            if (!string.IsNullOrEmpty(target))
                LeadWhenLoaded(target);

            if (DoorFlow.Instance != null)
                DoorFlow.Instance.Entered();
        }));
    }

    // ------------------------------------------------------- guiding, once loaded

    // Static: the preview is closed by the time the new trail's stops arrive, and
    // the same "Заведи ме" the next-stop card uses needs the stop to be loaded.
    static string _leadId;
    static float _leadUntil;

    /// <summary>
    /// Starts guidance to a stop as soon as the joined trail's stops are loaded,
    /// the way the next-stop card's "Заведи ме" does. Gives up after a minute:
    /// the map loads stops near the reader, and one far off may not come.
    /// </summary>
    static void LeadWhenLoaded(string stopId)
    {
        _leadId = stopId;
        _leadUntil = Time.realtimeSinceStartup + 60f;

        BuildingDatabase.OnBuildingsChanged -= TryLead;
        BuildingDatabase.OnBuildingsChanged += TryLead;
        TryLead();
    }

    static void TryLead()
    {
        if (string.IsNullOrEmpty(_leadId) || Time.realtimeSinceStartup > _leadUntil)
        {
            _leadId = null;
            BuildingDatabase.OnBuildingsChanged -= TryLead;
            return;
        }

        BuildingData stop = BuildingDatabase.GetById(_leadId);
        if (stop == null)
            return;

        _leadId = null;
        BuildingDatabase.OnBuildingsChanged -= TryLead;

        GuidanceService.Guide(stop);
        ProximityToast.Say(string.Format(AppStrings.Get("guide.toast"), stop.DisplayName));
    }

    static void Set(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on)
            go.SetActive(on);
    }
}
