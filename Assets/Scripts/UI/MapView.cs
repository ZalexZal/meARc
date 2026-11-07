using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The map the app opens on: the reader at the centre, markers where they really
/// are, north up, over OpenStreetMap tiles.
///
/// The projection is deliberately trivial - a local east/north offset in metres,
/// scaled to pixels. Everything on the map goes through <see cref="Project"/>,
/// including the tiles, which are placed by projecting their own corners. That is
/// what keeps a pin over the right building: both answers come from the same
/// question in the same units.
///
/// On redrawing: this used to lay itself out every frame, which wrote colours and
/// sizes into the canvas sixty times a second whether or not anything had moved.
/// A uGUI canvas rebuilds and re-batches in full when any element on it is dirtied,
/// so that one habit was rebuilding every panel, the nav bar and the sheet,
/// continuously. Now nothing is written unless it changed, and layout only runs
/// when something asked it to.
/// </summary>
public class MapView : MonoBehaviour, IDragHandler, IBeginDragHandler, IScrollHandler
{
    [Header("References")]
    public UserLocationService locationService;

    [Tooltip("Pins, the reader dot and the guidance ray are children of this, so " +
             "they clip to the map rather than to the panel.")]
    public RectTransform contentParent;

    public MapPinView pinPrefab;

    [Header("Reader")]
    public RectTransform userDot;

    [Tooltip("Faint fill at the reported horizontal accuracy, so the reader can " +
             "see how much the map is guessing.")]
    public Image accuracyCircle;

    [Tooltip("The edge of that circle, drawn as geometry. A scaled-up ring texture " +
             "turns into a blur the moment accuracy is poor, which is precisely " +
             "when the reader is looking at it.")]
    public RingGraphic accuracyRing;

    [Header("Guidance")]
    [Tooltip("Straight line from the reader to whatever they are being guided to. " +
             "Not a route - the app does not know the streets, and pretending it " +
             "did would send someone into a wall.")]
    public RectTransform guidanceRay;

    [Header("Chrome")]
    [Tooltip("Where the faint line through the stops, in the trail's order, is " +
             "drawn. Under the pins. Not drawn for a freefall trail, which has no order.")]
    public RectTransform routeParent;

    [Tooltip("The pill the legs are drawn with, for round ends and joins.")]
    public Sprite legSprite;

    public RectTransform scaleBar;
    public TextMeshProUGUI scaleLabel;
    public Button recentreButton;

    [Tooltip("What floats over the map at the top and the bottom: the trail pill " +
             "and the card. The reader is kept in the middle of what is left, so " +
             "a stop just south of them is not hidden under the card.")]
    public RectTransform coverTop;
    public RectTransform coverBottom;

    [Header("Zoom")]
    [Tooltip("Metres visible across the width of the map at the current zoom.")]
    public float metresAcross = 400f;
    public float minMetresAcross = 60f;
    public float maxMetresAcross = 4000f;

    [Tooltip("Above this many pins on screen, only the selected one is named. Two " +
             "labels crossing each other are worse than none.")]
    public int maxLabelledPins = 8;

    /// <summary>Raised when a pin is tapped.</summary>
    public Action<BuildingData> onPinSelected;

    /// <summary>Raised when the reader pans or zooms, so a recentre control can appear.</summary>
    public Action onViewMoved;

    /// <summary>
    /// Turned off while a sheet is open. Dragging is already blocked by the sheet
    /// scrim, but pinch reads raw touches and would otherwise zoom the map behind
    /// whatever the reader is reading.
    /// </summary>
    public bool interactable = true;

    readonly Dictionary<BuildingData, MapPinView> _pins =
        new Dictionary<BuildingData, MapPinView>();

    readonly List<BuildingData> _spent = new List<BuildingData>();

    RectTransform _rect;
    BuildingData _selected;

    // Where the map is looking, as an offset in metres from the origin.
    double _panEast;
    double _panNorth;
    bool _followsReader = true;

    // The point every offset is measured from. Held rather than read live, because
    // rewriting it every second is what makes a map shiver under GPS jitter.
    double _originLat;
    double _originLon;
    bool _hasOrigin;

    float _pinchStart;
    float _metresAtPinchStart;

    bool _dirty = true;
    Vector2 _lastSize;

    // Last values written to the canvas. Writing an unchanged colour still marks
    // the graphic dirty, which is the whole cost being avoided here.
    double _labelledStep = -1d;
    float _lastAccuracyRadius = -1f;
    Color _lastAccuracyColour;
    Color _lastRingColour;
    Color _lastRayColour;

    public bool FollowsReader { get { return _followsReader; } }

    public bool HasOrigin { get { return _hasOrigin; } }

    /// <summary>Bumped whenever the view changes, so layers over the map can skip work.</summary>
    public int Version { get; private set; }

    public Rect ViewRect
    {
        get { return _rect != null ? _rect.rect : new Rect(); }
    }

    public float PixelsPerMetre
    {
        get
        {
            float width = _rect != null ? _rect.rect.width : 0f;
            if (width <= 1f || metresAcross <= 0f)
                return 1f;

            return width / metresAcross;
        }
    }

    void Awake()
    {
        _rect = (RectTransform)transform;

        if (recentreButton != null)
            recentreButton.onClick.AddListener(Recentre);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (locationService != null)
            locationService.OnPoseUpdated += HandlePoseUpdated;

        BuildingDatabase.OnBuildingsChanged += Rebuild;
        ThemeManager.OnThemeChanged += Repaint;
        AppStrings.OnLanguageChanged += Repaint;
        GuidanceService.OnGuidanceChanged += HandleGuidanceChanged;
        NextStopCard.OnNextChanged += HandleProgress;
        InfoPanelController.OnClosed += Deselect;
        DiscoveryTracker.OnDiscoveryChanged += HandleProgress;
        MarkerTaskService.OnProgressChanged += HandleProgress;

        Rebuild();
        MarkDirty();
    }

    void OnDisable()
    {
        if (locationService != null)
            locationService.OnPoseUpdated -= HandlePoseUpdated;

        BuildingDatabase.OnBuildingsChanged -= Rebuild;
        ThemeManager.OnThemeChanged -= Repaint;
        AppStrings.OnLanguageChanged -= Repaint;
        GuidanceService.OnGuidanceChanged -= HandleGuidanceChanged;
        NextStopCard.OnNextChanged -= HandleProgress;
        InfoPanelController.OnClosed -= Deselect;
        DiscoveryTracker.OnDiscoveryChanged -= HandleProgress;
        MarkerTaskService.OnProgressChanged -= HandleProgress;
    }

    void Update()
    {
        HandlePinch();

        // A rotation or a fold changes the rect without anything else knowing.
        if (_rect != null && _rect.rect.size != _lastSize)
        {
            _lastSize = _rect.rect.size;
            MarkDirty();
        }

        if (!_dirty)
            return;

        _dirty = false;
        LayOut();
    }

    /// <summary>Asks for one more layout pass. Cheap, and idempotent within a frame.</summary>
    public void MarkDirty()
    {
        _dirty = true;
        Version++;
    }

    // --------------------------------------------------------------- projection

    /// <summary>Where a coordinate lands on the map, in this rect's local pixels.</summary>
    public Vector2 Project(double latitude, double longitude)
    {
        if (!_hasOrigin)
            return Vector2.zero;

        GeoMath.LocalOffsetMetres(_originLat, _originLon, latitude, longitude,
                                  out double east, out double north);

        float scale = PixelsPerMetre;
        return new Vector2((float)((east - _panEast) * scale),
                           (float)((north - _panNorth) * scale));
    }

    /// <summary>The inverse: what coordinate a point on the map is over.</summary>
    public void Unproject(Vector2 point, out double latitude, out double longitude)
    {
        latitude = 0d;
        longitude = 0d;

        if (!_hasOrigin)
            return;

        float scale = PixelsPerMetre;
        if (scale <= 0f)
            return;

        GeoMath.OffsetToLatLon(_originLat, _originLon,
                               point.x / scale + _panEast,
                               point.y / scale + _panNorth,
                               out latitude, out longitude);
    }

    /// <summary>What the middle of the map is over. False until there is a position.</summary>
    public bool TryGetViewCentre(out double latitude, out double longitude)
    {
        latitude = 0d;
        longitude = 0d;

        if (!_hasOrigin)
            return false;

        Unproject(Vector2.zero, out latitude, out longitude);
        return true;
    }

    // -------------------------------------------------------------------- input

    public void OnBeginDrag(PointerEventData eventData)
    {
        // A drag is a deliberate look elsewhere, so the map stops chasing the
        // reader until they ask for it back.
        _followsReader = false;
        Moved();
    }

    public void OnDrag(PointerEventData eventData)
    {
        float scale = PixelsPerMetre;
        if (scale <= 0f)
            return;

        _panEast -= eventData.delta.x / scale;
        _panNorth -= eventData.delta.y / scale;

        _followsReader = false;
        Moved();
    }

    public void OnScroll(PointerEventData eventData)
    {
        // Mouse wheel, so this is workable in the Editor. Pinch is the real gesture.
        SetZoom(metresAcross * (1f - eventData.scrollDelta.y * 0.1f));
    }

    void HandlePinch()
    {
        if (!interactable || Input.touchCount != 2)
        {
            _pinchStart = 0f;
            return;
        }

        float gap = (Input.GetTouch(0).position - Input.GetTouch(1).position).magnitude;

        if (_pinchStart <= 0f)
        {
            _pinchStart = gap;
            _metresAtPinchStart = metresAcross;
            return;
        }

        if (gap <= 1f)
            return;

        SetZoom(_metresAtPinchStart * (_pinchStart / gap));
    }

    void SetZoom(float metres)
    {
        float clamped = Mathf.Clamp(metres, minMetresAcross, maxMetresAcross);
        if (Mathf.Approximately(clamped, metresAcross))
            return;

        metresAcross = clamped;
        Moved();
    }

    /// <summary>
    /// Centres on a point at a given width, with or without a position. The demo
    /// uses it so its stops are in frame even when location is off.
    /// </summary>
    public void LookAt(double latitude, double longitude, float metres)
    {
        if (!_hasOrigin)
        {
            _originLat = latitude;
            _originLon = longitude;
            _hasOrigin = true;
        }

        _followsReader = false;
        GeoMath.LocalOffsetMetres(_originLat, _originLon, latitude, longitude,
                                  out _panEast, out _panNorth);
        metresAcross = Mathf.Clamp(metres, minMetresAcross, maxMetresAcross);
        Moved();
    }

    /// <summary>Back to the reader, at a zoom that includes the nearest marker.</summary>
    public void Recentre()
    {
        _followsReader = true;
        FitToNearest();
        SnapPanToReader();
        Moved();
    }

    void Moved()
    {
        MarkDirty();

        if (onViewMoved != null)
            onViewMoved();
    }

    /// <summary>
    /// Chooses a zoom that puts the closest marker comfortably inside the view. A
    /// map showing nothing is worse than a map zoomed further out than asked for.
    /// </summary>
    public void FitToNearest()
    {
        if (locationService == null || !locationService.HasValidPose)
            return;

        double nearest = double.MaxValue;

        var all = BuildingDatabase.AllBuildings;
        for (int i = 0; i < all.Count; i++)
        {
            double distance = locationService.DistanceMetresTo(all[i]);
            if (distance >= 0d && distance < nearest)
                nearest = distance;
        }

        if (nearest == double.MaxValue)
            return;

        // Two and a half times the distance leaves the marker well inside the
        // frame rather than kissing the edge.
        metresAcross = Mathf.Clamp((float)nearest * 2.5f, minMetresAcross, maxMetresAcross);
    }

    // --------------------------------------------------------------------- pins

    void HandlePoseUpdated()
    {
        if (locationService == null || !locationService.HasValidPose)
            return;

        if (!_hasOrigin)
        {
            _originLat = locationService.Latitude;
            _originLon = locationService.Longitude;
            _hasOrigin = true;

            FitToNearest();
        }

        if (_followsReader)
            SnapPanToReader();

        MarkDirty();
    }

    void SnapPanToReader()
    {
        if (locationService == null || !locationService.HasValidPose || !_hasOrigin)
            return;

        GeoMath.LocalOffsetMetres(_originLat, _originLon,
                                  locationService.Latitude, locationService.Longitude,
                                  out _panEast, out _panNorth);

        _panNorth -= ClearMiddle() / PixelsPerMetre;
    }

    static readonly Vector3[] Corners = new Vector3[4];

    /// <summary>
    /// How far above the rect's middle the middle of the uncovered map is, in
    /// local pixels. Zero when nothing is covering it.
    /// </summary>
    float ClearMiddle()
    {
        if (_rect == null)
            return 0f;

        Rect r = _rect.rect;
        float top = r.yMax;
        float bottom = r.yMin;

        if (coverTop != null && coverTop.gameObject.activeInHierarchy)
        {
            coverTop.GetWorldCorners(Corners);
            top = Mathf.Min(top, _rect.InverseTransformPoint(Corners[0]).y);
        }

        if (coverBottom != null && coverBottom.gameObject.activeInHierarchy)
        {
            coverBottom.GetWorldCorners(Corners);
            bottom = Mathf.Max(bottom, _rect.InverseTransformPoint(Corners[1]).y);
        }

        return top > bottom ? (top + bottom) * 0.5f - r.center.y : 0f;
    }

    void HandleProgress()
    {
        ApplySelection();
        MarkDirty();
    }

    void HandleGuidanceChanged()
    {
        ApplySelection();
        MarkDirty();
    }

    /// <summary>Creates and destroys pins to match the database. Called rarely.</summary>
    public void Rebuild()
    {
        if (pinPrefab == null || contentParent == null)
            return;

        var all = BuildingDatabase.AllBuildings;

        _spent.Clear();
        foreach (KeyValuePair<BuildingData, MapPinView> pair in _pins)
        {
            bool stillThere = false;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == pair.Key)
                {
                    stillThere = true;
                    break;
                }
            }

            if (!stillThere)
                _spent.Add(pair.Key);
        }

        for (int i = 0; i < _spent.Count; i++)
        {
            MapPinView spent = _pins[_spent[i]];
            _pins.Remove(_spent[i]);

            if (spent != null)
                Destroy(spent.gameObject);
        }

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] == null || _pins.ContainsKey(all[i]))
                continue;

            MapPinView pin = Instantiate(pinPrefab, contentParent);
            pin.gameObject.name = "Pin - " + all[i].DisplayName;
            pin.Bind(all[i], RaiseSelected);
            // One after another, 40 ms apart, never longer than half a second.
            pin.Appear(Mathf.Min(_pins.Count * 0.04f, 0.5f));

            _pins.Add(all[i], pin);
        }

        GuidanceService.Validate();
        ApplySelection();
        MarkDirty();

        // A trail just opened: frame its nearest stop, as the first fix does.
        // Joining one kept the zoom the last trail needed - 25 m across, with all
        // five of the new trail's stops 300 m away and off the screen. The demo
        // frames its own stops.
        string trail = TrailService.Instance != null ? TrailService.Instance.SelectedId : null;

        if (!DemoTrail.Active && all.Count > 0 && trail != _framedTrail)
        {
            _framedTrail = trail;
            Recentre();
        }
    }

    string _framedTrail;

    void Repaint()
    {
        // Everything cached below is now in the wrong theme or the wrong language.
        _labelledStep = -1d;
        _lastAccuracyRadius = -1f;
        _lastAccuracyColour = default;
        _lastRingColour = default;
        _lastRayColour = default;

        foreach (KeyValuePair<BuildingData, MapPinView> pair in _pins)
        {
            if (pair.Value != null)
                pair.Value.Refresh();
        }

        ApplySelection();
        MarkDirty();
    }

    /// <summary>The sheet is gone, so no pin is the one being looked at.</summary>
    void Deselect()
    {
        if (_selected == null)
            return;

        Select(null);
    }

    public void Select(BuildingData building)
    {
        _selected = building;
        ApplySelection();
        MarkDirty();
    }

    void ApplySelection()
    {
        BuildingData guided = GuidanceService.Target;

        // Past this many pins the names collide with each other, so only the pin
        // being talked about keeps one.
        bool labels = _pins.Count <= maxLabelledPins;

        foreach (KeyValuePair<BuildingData, MapPinView> pair in _pins)
        {
            if (pair.Value == null)
                continue;

            pair.Value.SetLabelAllowed(labels);
            pair.Value.SetSelected(pair.Key == _selected || pair.Key == guided);

            // Done, next or later, as Daylight draws them. The card decides
            // which stop is next; the map only draws it.
            pair.Value.SetState(pair.Key == NextStopCard.Current
                ? MapPinView.State.Next
                : NextStopCard.Done(pair.Key) ? MapPinView.State.Done : MapPinView.State.Later);
        }
    }

    void RaiseSelected(BuildingData building)
    {
        Select(building);

        if (onPinSelected != null)
            onPinSelected(building);
    }

    // ------------------------------------------------------------------ drawing

    void LayOut()
    {
        LayOutRoute();
        LayOutPins();
        LayOutReader();
        LayOutGuidance();
        LayOutScale();
    }

    void LayOutPins()
    {
        foreach (KeyValuePair<BuildingData, MapPinView> pair in _pins)
        {
            if (pair.Value == null)
                continue;

            Vector2 position = Project(pair.Key.latitude, pair.Key.longitude);
            RectTransform rt = (RectTransform)pair.Value.transform;

            if (rt.anchoredPosition != position)
                rt.anchoredPosition = position;

            // Hidden rather than clamped to the edge. A pin pinned to the frame
            // claims a position it does not have.
            bool visible = _hasOrigin && Within(position, 60f);
            if (pair.Value.gameObject.activeSelf != visible)
                pair.Value.gameObject.SetActive(visible);
        }
    }

    readonly List<RectTransform> _legs = new List<RectTransform>();
    readonly List<BuildingData> _route = new List<BuildingData>();
    Color _legColour;

    /// <summary>
    /// The trail's order, drawn: a hairline from each stop to the next. Not a
    /// route - the app does not know the streets - but the sequence the creator
    /// chose, which is what turns five dots into a walk.
    /// </summary>
    void LayOutRoute()
    {
        if (routeParent == null)
            return;

        _route.Clear();

        TrailService trails = TrailService.Instance;
        // No route line on a trail with no order: Freefall and Educational alike.
        bool freefall = trails != null && trails.Selected != null && !TrailService.IsOrdered(trails.Selected.mode);

        if (!freefall)
        {
            var all = BuildingDatabase.AllBuildings;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].step > 0)
                    _route.Add(all[i]);
            }

            _route.Sort((a, b) => a.step.CompareTo(b.step));
        }

        int needed = _hasOrigin ? Mathf.Max(0, _route.Count - 1) : 0;

        Color colour = ThemeManager.IsDark
            ? new Color(0.96f, 0.96f, 0.97f, 0.24f)
            : new Color(0.114f, 0.114f, 0.122f, 0.20f);

        while (_legs.Count < needed)
        {
            GameObject go = new GameObject("Leg", typeof(RectTransform));
            go.layer = routeParent.gameObject.layer;
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(routeParent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            Image image = go.AddComponent<Image>();
            image.raycastTarget = false;
            image.color = colour;

            if (legSprite != null)
            {
                image.sprite = legSprite;
                image.type = Image.Type.Sliced;
                image.pixelsPerUnitMultiplier = 160f / 11f;
            }

            _legs.Add(rt);
        }

        bool recolour = colour != _legColour;
        _legColour = colour;

        for (int i = 0; i < _legs.Count; i++)
        {
            bool on = i < needed;
            if (_legs[i].gameObject.activeSelf != on)
                _legs[i].gameObject.SetActive(on);

            if (!on)
                continue;

            if (recolour)
                _legs[i].GetComponent<Image>().color = colour;

            Vector2 from = Project(_route[i].latitude, _route[i].longitude);
            Vector2 to = Project(_route[i + 1].latitude, _route[i + 1].longitude);
            Vector2 delta = to - from;

            _legs[i].anchoredPosition = from + delta * 0.5f;
            // Overlapping by the line's width, so each joint is round.
            _legs[i].sizeDelta = new Vector2(delta.magnitude + 11f, 11f);
            _legs[i].localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
    }

    bool Within(Vector2 point, float slack)
    {
        if (_rect == null)
            return true;

        Rect r = _rect.rect;
        return point.x >= r.xMin - slack && point.x <= r.xMax + slack &&
               point.y >= r.yMin - slack && point.y <= r.yMax + slack;
    }

    void LayOutReader()
    {
        bool known = _hasOrigin && locationService != null && locationService.HasValidPose;

        if (userDot != null)
        {
            if (userDot.gameObject.activeSelf != known)
                userDot.gameObject.SetActive(known);

            if (known)
            {
                Vector2 position = Project(locationService.Latitude, locationService.Longitude);
                if (userDot.anchoredPosition != position)
                    userDot.anchoredPosition = position;
            }
        }

        if (accuracyCircle == null)
            return;

        // Only worth drawing when it is big enough to mean something.
        float radius = known
            ? (float)locationService.HorizontalAccuracy * PixelsPerMetre
            : 0f;

        bool show = known && radius > 12f;

        if (accuracyCircle.gameObject.activeSelf != show)
            accuracyCircle.gameObject.SetActive(show);

        if (!show)
            return;

        Vector2 centre = userDot != null ? userDot.anchoredPosition : Vector2.zero;
        if (accuracyCircle.rectTransform.anchoredPosition != centre)
            accuracyCircle.rectTransform.anchoredPosition = centre;

        radius = Mathf.Min(radius, _rect.rect.width);

        if (!Mathf.Approximately(radius, _lastAccuracyRadius))
        {
            _lastAccuracyRadius = radius;
            accuracyCircle.rectTransform.sizeDelta = new Vector2(radius * 2f, radius * 2f);
        }

        // A wash with no edge: Daylight draws the uncertainty as a tint, and the
        // dot and the pins still read through it.
        Color tint = ThemeManager.Get(ThemeRole.Accent);
        tint.a = 0.12f;

        if (tint != _lastAccuracyColour)
        {
            _lastAccuracyColour = tint;
            accuracyCircle.color = tint;
        }

        if (accuracyRing == null)
            return;

        if (accuracyRing.gameObject.activeSelf != show)
            accuracyRing.gameObject.SetActive(show);

        accuracyRing.rectTransform.anchoredPosition = centre;
        accuracyRing.rectTransform.sizeDelta = new Vector2(radius * 2f, radius * 2f);

        Color edge = ThemeManager.Get(ThemeRole.Accent);
        edge.a = 0.5f;

        if (edge != _lastRingColour)
        {
            _lastRingColour = edge;
            accuracyRing.color = edge;
        }
    }

    void LayOutGuidance()
    {
        if (guidanceRay == null)
            return;

        BuildingData target = GuidanceService.Target;

        bool show = target != null && _hasOrigin &&
                    locationService != null && locationService.HasValidPose;

        if (guidanceRay.gameObject.activeSelf != show)
            guidanceRay.gameObject.SetActive(show);

        if (!show)
            return;

        Vector2 from = Project(locationService.Latitude, locationService.Longitude);
        Vector2 to = Project(target.latitude, target.longitude);
        Vector2 delta = to - from;

        Vector2 middle = from + delta * 0.5f;
        if (guidanceRay.anchoredPosition != middle)
            guidanceRay.anchoredPosition = middle;

        Vector2 size = new Vector2(delta.magnitude, guidanceRay.sizeDelta.y);
        if (guidanceRay.sizeDelta != size)
            guidanceRay.sizeDelta = size;

        Quaternion rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        if (guidanceRay.localRotation != rotation)
            guidanceRay.localRotation = rotation;

        Image ray = guidanceRay.GetComponent<Image>();
        if (ray == null)
            return;

        Color tint = ThemeManager.Get(ThemeRole.Accent);

        if (tint != _lastRayColour)
        {
            _lastRayColour = tint;
            ray.color = tint;
        }
    }

    /// <summary>
    /// The scale bar is what makes a distance on this map checkable, so it always
    /// shows a round number of metres rather than a round number of pixels.
    /// </summary>
    void LayOutScale()
    {
        if (scaleBar == null)
            return;

        float scale = PixelsPerMetre;
        if (scale <= 0f)
            return;

        double chosen = ScaleStepMetres();

        Vector2 size = new Vector2((float)(chosen * scale), scaleBar.sizeDelta.y);
        if (scaleBar.sizeDelta != size)
            scaleBar.sizeDelta = size;

        if (scaleLabel == null || Math.Abs(chosen - _labelledStep) < 0.001d)
            return;

        _labelledStep = chosen;
        scaleLabel.text = AppStrings.FormatDistance(chosen);
    }

    /// <summary>The largest round distance that fits comfortably across the map.</summary>
    double ScaleStepMetres()
    {
        float scale = PixelsPerMetre;
        if (scale <= 0f || _rect == null)
            return 100d;

        double wanted = _rect.rect.width * 0.28f / scale;

        double[] steps = { 10d, 20d, 25d, 50d, 100d, 200d, 250d, 500d, 1000d, 2000d };
        double chosen = steps[0];

        for (int i = 0; i < steps.Length; i++)
        {
            if (steps[i] <= wanted)
                chosen = steps[i];
        }

        return chosen;
    }
}
