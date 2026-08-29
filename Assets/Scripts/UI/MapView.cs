using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The map the app opens on: the reader at the centre, markers where they really
/// are, north up.
///
/// It is drawn rather than tiled. Nothing is fetched, so it works with no signal
/// and costs nothing to render, and the price - no streets - is paid back by the
/// scale bar and the distance rings, which answer "how far" more directly than a
/// tile of a town centre at 200 m does. The projection is deliberately trivial: a
/// local east/north offset in metres, scaled to pixels. Swapping in raster tiles
/// later means adding a tile layer under the content and leaving every other line
/// in this file alone, because pins are placed from metres, not from a tile's
/// pixel grid.
/// </summary>
public class MapView : MonoBehaviour, IDragHandler, IBeginDragHandler, IScrollHandler
{
    [Header("References")]
    public UserLocationService locationService;

    [Tooltip("Pins, the user dot and the guidance ray are all children of this, so " +
             "they clip to the map rather than to the panel.")]
    public RectTransform contentParent;

    public MapPinView pinPrefab;

    [Header("Reader")]
    public RectTransform userDot;

    [Tooltip("Drawn at the reported horizontal accuracy, so the reader can see how " +
             "much the map is guessing.")]
    public Image accuracyCircle;

    [Header("Guidance")]
    [Tooltip("Straight line from the reader to whatever they are being guided to. " +
             "Not a route - the app does not know the streets, and pretending it " +
             "did would send someone into a wall.")]
    public RectTransform guidanceRay;

    [Header("Chrome")]
    [Tooltip("Rings drawn around the reader at the scale bar's distance and twice " +
             "it. They replace a street grid: they move with the reader, so the map " +
             "visibly responds to walking, and they answer \"how far is that\" " +
             "without the reader measuring anything.")]
    public Image[] distanceRings;

    public RectTransform scaleBar;
    public TextMeshProUGUI scaleLabel;
    public Button recentreButton;

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

    readonly Dictionary<BuildingData, MapPinView> _pins =
        new Dictionary<BuildingData, MapPinView>();

    readonly List<BuildingData> _spent = new List<BuildingData>();

    RectTransform _rect;
    BuildingData _selected;

    // Where the map is looking, as an offset in metres from the origin. Following
    // the reader means this tracks their position.
    double _panEast;
    double _panNorth;
    bool _followsReader = true;

    // The point every offset is measured from. Held rather than read live, because
    // rewriting it every second is what makes a drawn map shiver under GPS jitter.
    double _originLat;
    double _originLon;
    bool _hasOrigin;

    float _pinchStart;
    float _metresAtPinchStart;

    // The last step the scale bar was labelled with. Held so the label is only
    // rewritten when the number changes rather than once a frame - LayOut runs
    // every frame, and formatting a string every frame is free garbage.
    double _labelledStep = -1d;

    public bool FollowsReader { get { return _followsReader; } }

    public bool HasOrigin { get { return _hasOrigin; } }

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
        GuidanceService.OnGuidanceChanged += ApplySelection;

        Rebuild();
    }

    void OnDisable()
    {
        if (locationService != null)
            locationService.OnPoseUpdated -= HandlePoseUpdated;

        BuildingDatabase.OnBuildingsChanged -= Rebuild;
        ThemeManager.OnThemeChanged -= Repaint;
        AppStrings.OnLanguageChanged -= Repaint;
        GuidanceService.OnGuidanceChanged -= ApplySelection;
    }

    void Update()
    {
        HandlePinch();
        LayOut();
    }

    // --------------------------------------------------------------- projection

    float PixelsPerMetre
    {
        get
        {
            float width = _rect != null ? _rect.rect.width : 0f;
            if (width <= 1f || metresAcross <= 0f)
                return 1f;

            return width / metresAcross;
        }
    }

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
        if (Input.touchCount != 2)
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
    }

    void SnapPanToReader()
    {
        if (locationService == null || !locationService.HasValidPose || !_hasOrigin)
            return;

        GeoMath.LocalOffsetMetres(_originLat, _originLon,
                                  locationService.Latitude, locationService.Longitude,
                                  out _panEast, out _panNorth);
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

            _pins.Add(all[i], pin);
        }

        GuidanceService.Validate();
        ApplySelection();
    }

    void Repaint()
    {
        // The cached scale label is in the old language until this is cleared.
        _labelledStep = -1d;

        foreach (KeyValuePair<BuildingData, MapPinView> pair in _pins)
        {
            if (pair.Value != null)
                pair.Value.Refresh();
        }

        ApplySelection();
    }

    public void Select(BuildingData building)
    {
        _selected = building;
        ApplySelection();
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
        LayOutPins();
        LayOutReader();
        LayOutRings();
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
            ((RectTransform)pair.Value.transform).anchoredPosition = position;

            // Hidden rather than clamped to the edge. A pin pinned to the frame
            // claims a position it does not have.
            pair.Value.gameObject.SetActive(_hasOrigin && Within(position, 60f));
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
            userDot.gameObject.SetActive(known);

            if (known)
            {
                userDot.anchoredPosition =
                    Project(locationService.Latitude, locationService.Longitude);
            }
        }

        if (accuracyCircle == null)
            return;

        // Only worth drawing when it is big enough to mean something.
        float radius = known
            ? (float)locationService.HorizontalAccuracy * PixelsPerMetre
            : 0f;

        bool show = known && radius > 12f;
        accuracyCircle.gameObject.SetActive(show);

        if (!show)
            return;

        accuracyCircle.rectTransform.anchoredPosition =
            userDot != null ? userDot.anchoredPosition : Vector2.zero;

        radius = Mathf.Min(radius, _rect.rect.width);
        accuracyCircle.rectTransform.sizeDelta = new Vector2(radius * 2f, radius * 2f);

        Color tint = ThemeManager.Get(ThemeRole.Accent);
        tint.a = 0.14f;
        accuracyCircle.color = tint;
    }

    /// <summary>
    /// Sized from the same rounded step the scale bar uses, so the ring is always a
    /// number the reader has just read rather than an arbitrary circle.
    /// </summary>
    void LayOutRings()
    {
        if (distanceRings == null || distanceRings.Length == 0)
            return;

        bool known = _hasOrigin && locationService != null && locationService.HasValidPose;
        Vector2 centre = known && userDot != null ? userDot.anchoredPosition : Vector2.zero;

        float scale = PixelsPerMetre;
        double step = ScaleStepMetres();

        for (int i = 0; i < distanceRings.Length; i++)
        {
            Image ring = distanceRings[i];
            if (ring == null)
                continue;

            ring.gameObject.SetActive(known);

            if (!known)
                continue;

            float radius = (float)(step * (i + 1)) * scale;

            ring.rectTransform.anchoredPosition = centre;
            ring.rectTransform.sizeDelta = new Vector2(radius * 2f, radius * 2f);

            Color tint = ThemeManager.Get(ThemeRole.Line);
            tint.a = 0.55f;
            ring.color = tint;
        }
    }

    void LayOutGuidance()
    {
        if (guidanceRay == null)
            return;

        BuildingData target = GuidanceService.Target;

        bool show = target != null && _hasOrigin &&
                    locationService != null && locationService.HasValidPose;

        guidanceRay.gameObject.SetActive(show);

        if (!show)
            return;

        Vector2 from = Project(locationService.Latitude, locationService.Longitude);
        Vector2 to = Project(target.latitude, target.longitude);
        Vector2 delta = to - from;

        guidanceRay.anchoredPosition = from + delta * 0.5f;
        guidanceRay.sizeDelta = new Vector2(delta.magnitude, guidanceRay.sizeDelta.y);
        guidanceRay.localRotation =
            Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);

        Image ray = guidanceRay.GetComponent<Image>();
        if (ray != null)
        {
            Color tint = ThemeManager.Get(ThemeRole.Accent);
            tint.a = 0.75f;
            ray.color = tint;
        }
    }

    /// <summary>
    /// The scale bar is the reason a map with no streets is still readable, so it
    /// always shows a round number of metres rather than a round number of pixels.
    /// </summary>
    void LayOutScale()
    {
        if (scaleBar == null)
            return;

        float scale = PixelsPerMetre;
        if (scale <= 0f)
            return;

        double chosen = ScaleStepMetres();

        scaleBar.sizeDelta = new Vector2((float)(chosen * scale), scaleBar.sizeDelta.y);

        if (scaleLabel == null || System.Math.Abs(chosen - _labelledStep) < 0.001d)
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
