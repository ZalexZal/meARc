using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The trail as a picture you can move around in.
///
/// The nodes are not laid out by a force between them. A trail is a sequence -
/// marker one joins two joins three and nothing else - so a force-directed
/// layout would spend its effort destroying the one relationship the data has,
/// and hand back a wiggly line with the numbers in the wrong places. They sit on
/// their real coordinates instead, projected flat, which means the shape on
/// screen is the shape of the walk: a loop looks like a loop, and a straight
/// street looks like a straight street.
///
/// Drag to move, pinch to zoom, and it fits itself to the trail whenever it
/// opens. The fit matters more than the gestures - most people will look at it
/// and never touch it, and what they should see is the whole walk.
/// </summary>
public class TrailGraphView : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    /// <summary>Metres per degree of latitude. Good enough for a town.</summary>
    const double MetresPerDegree = 111320d;

    [Header("Parts")]
    [Tooltip("The window. Clips, and receives the drag.")]
    public RectTransform viewport;

    [Tooltip("What actually moves. Nodes and edges are its children.")]
    public RectTransform content;

    public TrailGraphNodeView nodePrefab;

    [Header("Empty")]
    public GameObject emptyGroup;

    [Header("Feel")]
    [Tooltip("Screen pixels per metre at the fitted zoom is worked out; this is " +
             "how much of the viewport the trail is allowed to fill.")]
    public float fitMargin = 0.78f;

    public float minZoom = 0.35f;
    public float maxZoom = 4f;

    /// <summary>Opens the marker on the map. Set by whoever builds this screen.</summary>
    public Action<BuildingData> onOpen;

    readonly List<TrailGraphNodeView> _nodes = new List<TrailGraphNodeView>();
    readonly List<RectTransform> _edges = new List<RectTransform>();
    readonly List<BuildingData> _ordered = new List<BuildingData>();

    /// <summary>Each marker in metres from the middle of the trail, before zoom.</summary>
    Vector2[] _places = System.Array.Empty<Vector2>();

    float _zoom = 1f;
    float _fitted = 1f;
    Vector2 _grabbed;
    float _pinchStart;
    float _zoomAtPinchStart;

    void OnEnable()
    {
        DiscoveryTracker.OnDiscoveryChanged += Paint;
        BuildingDatabase.OnBuildingsChanged += Rebuild;
        AppStrings.OnLanguageChanged += Rebuild;
        ThemeManager.OnThemeChanged += Paint;

        Rebuild();
    }

    void OnDisable()
    {
        DiscoveryTracker.OnDiscoveryChanged -= Paint;
        BuildingDatabase.OnBuildingsChanged -= Rebuild;
        AppStrings.OnLanguageChanged -= Rebuild;
        ThemeManager.OnThemeChanged -= Paint;
    }

    // ------------------------------------------------------------------ layout

    public void Rebuild()
    {
        Clear();

        _ordered.Clear();

        IReadOnlyList<BuildingData> all = BuildingDatabase.AllBuildings;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] != null)
                _ordered.Add(all[i]);
        }

        // Trail order, which is what the numbers on the nodes mean. step is 0 for
        // a marker placed before the column existed, and those go last rather
        // than first - a stray zero at the head of the walk is worse than one at
        // the end of it.
        _ordered.Sort((a, b) =>
        {
            int sa = a.step > 0 ? a.step : int.MaxValue;
            int sb = b.step > 0 ? b.step : int.MaxValue;

            if (sa != sb)
                return sa.CompareTo(sb);

            return string.CompareOrdinal(a.buildingId, b.buildingId);
        });

        bool any = _ordered.Count > 0;

        if (emptyGroup != null)
            emptyGroup.SetActive(!any);

        if (content != null)
            content.gameObject.SetActive(any);

        if (!any || nodePrefab == null || content == null)
            return;

        // --- where each one sits, in metres from the middle of the trail
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLng = double.MaxValue, maxLng = double.MinValue;

        for (int i = 0; i < _ordered.Count; i++)
        {
            minLat = Math.Min(minLat, _ordered[i].latitude);
            maxLat = Math.Max(maxLat, _ordered[i].latitude);
            minLng = Math.Min(minLng, _ordered[i].longitude);
            maxLng = Math.Max(maxLng, _ordered[i].longitude);
        }

        double centreLat = (minLat + maxLat) * 0.5d;
        double centreLng = (minLng + maxLng) * 0.5d;

        // Longitude degrees shrink towards the poles. Without this a trail in
        // Bulgaria comes out stretched sideways by about a quarter.
        double lngScale = Math.Cos(centreLat * Math.PI / 180d);

        _places = new Vector2[_ordered.Count];
        Vector2[] places = _places;
        float spanX = 0f, spanY = 0f;

        for (int i = 0; i < _ordered.Count; i++)
        {
            places[i] = new Vector2(
                (float)((_ordered[i].longitude - centreLng) * lngScale * MetresPerDegree),
                (float)((_ordered[i].latitude - centreLat) * MetresPerDegree));

            spanX = Mathf.Max(spanX, Mathf.Abs(places[i].x) * 2f);
            spanY = Mathf.Max(spanY, Mathf.Abs(places[i].y) * 2f);
        }

        // A trail of one, or several markers on the same doorstep, would divide
        // by nothing. Twenty metres is an arbitrary floor that keeps the fit sane.
        spanX = Mathf.Max(spanX, 20f);
        spanY = Mathf.Max(spanY, 20f);

        Rect window = viewport != null ? viewport.rect : new Rect(0f, 0f, 900f, 900f);

        _fitted = Mathf.Min(window.width / spanX, window.height / spanY) * fitMargin;
        _zoom = _fitted;

        for (int i = 0; i < _ordered.Count; i++)
        {
            TrailGraphNodeView node = Instantiate(nodePrefab, content);
            BuildingData captured = _ordered[i];

            node.Bind(captured, i + 1, DiscoveryTracker.IsDiscovered(captured),
                      () => { if (onOpen != null) onOpen(captured); });

            RectTransform rt = (RectTransform)node.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

            _nodes.Add(node);
        }

        // Edges after nodes in the list but before them in draw order, so a line
        // never crosses over the disc it arrives at.
        for (int i = 1; i < _ordered.Count; i++)
            _edges.Add(MakeEdge());

        content.anchoredPosition = Vector2.zero;
        ApplyZoom();
        Paint();
    }

    RectTransform MakeEdge()
    {
        GameObject go = new GameObject("Edge", typeof(RectTransform), typeof(Image));
        RectTransform rt = (RectTransform)go.transform;
        rt.SetParent(content, false);
        rt.SetAsFirstSibling();

        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        Image image = go.GetComponent<Image>();
        image.raycastTarget = false;

        return rt;
    }

    void Clear()
    {
        for (int i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i] != null)
                Remove(_nodes[i].gameObject);
        }

        for (int i = 0; i < _edges.Count; i++)
        {
            if (_edges[i] != null)
                Remove(_edges[i].gameObject);
        }

        _nodes.Clear();
        _edges.Clear();
    }

    /// <summary>
    /// Unparented first, because Destroy is deferred to the end of the frame and a
    /// rebuild in between would measure children on their way out.
    ///
    /// DestroyImmediate outside play mode, because Destroy outside play mode does
    /// nothing at all - which is how the preview came back with one theme's
    /// colours baked into the other's screenshot.
    /// </summary>
    static void Remove(GameObject go)
    {
        go.transform.SetParent(null, false);

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }

    /// <summary>Colours only. Cheap enough to run whenever anything is found.</summary>
    void Paint()
    {
        Color line = ThemeManager.Get(ThemeRole.Line);

        for (int i = 0; i < _edges.Count; i++)
        {
            if (_edges[i] == null)
                continue;

            Image image = _edges[i].GetComponent<Image>();

            if (image != null)
                image.color = line;
        }

        for (int i = 0; i < _nodes.Count && i < _ordered.Count; i++)
        {
            if (_nodes[i] == null)
                continue;

            BuildingData captured = _ordered[i];

            _nodes[i].Bind(captured, i + 1, DiscoveryTracker.IsDiscovered(captured),
                           () => { if (onOpen != null) onOpen(captured); });
        }
    }

    // ------------------------------------------------------------------ moving

    public void OnBeginDrag(PointerEventData eventData)
    {
        _grabbed = content != null ? content.anchoredPosition : Vector2.zero;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (content == null)
            return;

        // The canvas scales to the device, so a finger's travel in screen pixels
        // is not the same number of canvas units. delta is already in screen
        // pixels; dividing by the canvas scale puts it back into ours.
        float scale = GetComponentInParent<Canvas>() != null
            ? GetComponentInParent<Canvas>().scaleFactor
            : 1f;

        _grabbed += eventData.delta / Mathf.Max(scale, 0.0001f);
        content.anchoredPosition = _grabbed;
    }

    void Update()
    {
        // Two fingers, and only while this screen is the one being touched.
        if (Input.touchCount != 2 || content == null)
        {
            _pinchStart = 0f;
            return;
        }

        Touch a = Input.GetTouch(0);
        Touch b = Input.GetTouch(1);
        float gap = Vector2.Distance(a.position, b.position);

        if (_pinchStart <= 0f)
        {
            _pinchStart = gap;
            _zoomAtPinchStart = _zoom;
            return;
        }

        if (_pinchStart > 1f)
        {
            _zoom = Mathf.Clamp(_zoomAtPinchStart * (gap / _pinchStart),
                                _fitted * minZoom, _fitted * maxZoom);
            ApplyZoom();
        }
    }

    /// <summary>
    /// Zoom spreads the markers out; it does not scale them.
    ///
    /// Scaling the whole picture is one line and is wrong here: a trail across a
    /// town fits at about a third, and a third of this type is unreadable. The
    /// discs and their names stay the size a thumb and an eye need, and the
    /// distances between them are what changes.
    /// </summary>
    void ApplyZoom()
    {
        for (int i = 0; i < _nodes.Count && i < _places.Length; i++)
        {
            if (_nodes[i] != null)
                ((RectTransform)_nodes[i].transform).anchoredPosition = _places[i] * _zoom;
        }

        for (int i = 0; i < _edges.Count; i++)
        {
            if (_edges[i] == null || i + 1 >= _places.Length)
                continue;

            Vector2 from = _places[i] * _zoom;
            Vector2 to = _places[i + 1] * _zoom;
            Vector2 along = to - from;

            _edges[i].anchoredPosition = (from + to) * 0.5f;
            _edges[i].sizeDelta = new Vector2(along.magnitude, 3f);
            _edges[i].localRotation =
                Quaternion.Euler(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg);
        }
    }

    /// <summary>Back to seeing the whole walk. Wired to a button.</summary>
    public void Fit()
    {
        _zoom = _fitted;

        if (content != null)
            content.anchoredPosition = Vector2.zero;

        ApplyZoom();
    }
}
