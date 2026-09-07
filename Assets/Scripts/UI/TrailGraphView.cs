using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The trail as a station map.
///
/// A metro map throws the real geography away on purpose. What a passenger needs
/// is the order, which stop is next, and where they are along the line - and a
/// schematic gives all three at a glance, where a picture drawn to scale gives
/// none of them and puts two stops on top of each other whenever the walk
/// doubles back.
///
/// So: one line, the markers evenly spaced along it in trail order, and a mark
/// between the last one found and the one ahead saying you are here. Drag to
/// move along it. The map screen holds the geography; this holds the sequence.
/// </summary>
public class TrailGraphView : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    /// <summary>Distance between one stop and the next, down the line.</summary>
    const float Spacing = 190f;

    /// <summary>
    /// How far the stop's disc sits left of its row's centre.
    ///
    /// The row is disc-then-name, so its middle is somewhere in the name. The
    /// line has to run through the discs, so the places are the discs and the
    /// rows are hung off them - not the other way round, which drew the line
    /// straight down the middle of the text.
    /// </summary>
    const float KnobOffsetX = -330f;

    [Header("Parts")]
    [Tooltip("The window. Clips, and receives the drag.")]
    public RectTransform viewport;

    [Tooltip("What actually moves. Nodes and edges are its children.")]
    public RectTransform content;

    public TrailGraphNodeView nodePrefab;

    [Header("Empty")]
    public GameObject emptyGroup;

    [Header("You are here")]
    [Tooltip("The mark on the line between the last stop found and the one " +
             "ahead. Telling you which way you are facing along the line is a " +
             "station map's whole job.")]
    public RectTransform herePip;
    public TMPro.TextMeshProUGUI hereLabel;

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

        // --- evenly down the line, oldest at the top
        _places = new Vector2[_ordered.Count];

        float run = (_ordered.Count - 1) * Spacing;

        // The line runs down the left of the card so the names have the width to
        // the right of it, the way a station's name reads off its stop.
        for (int i = 0; i < _ordered.Count; i++)
            _places[i] = new Vector2(KnobOffsetX, run * 0.5f - i * Spacing);

        // Nothing to fit: the spacing is fixed and the line is scrolled rather
        // than scaled. Zoom stays at one so a stop is always the same size,
        // which is the whole point of a schematic.
        _fitted = 1f;
        _zoom = 1f;

        for (int i = 0; i < _ordered.Count; i++)
        {
            TrailGraphNodeView node = Instantiate(nodePrefab, content);
            BuildingData captured = _ordered[i];

            int at = i;

            node.Bind(captured, i + 1, DiscoveryTracker.IsDiscovered(captured), Shut(at),
                      () => { if (onOpen != null) onOpen(captured); });

            RectTransform rt = (RectTransform)node.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);

            _nodes.Add(node);
        }

        // Edges after nodes in the list but before them in draw order, so a line
        // never crosses over the disc it arrives at.
        for (int i = 1; i < _ordered.Count; i++)
            _edges.Add(MakeEdge());

        PlaceHere();

        // Opened at the walker rather than at the beginning. On a trail half
        // walked, the top of the line is the part they no longer need.
        content.anchoredPosition = OpenedAt();

        ApplyZoom();
        Paint();
    }

    /// <summary>
    /// Whether this marker is one a guided trail has not opened yet.
    ///
    /// Guided trails run in order, so everything past the next one is shut. Every
    /// other mode lets a walker take them in any order and never shuts anything -
    /// a CTF board where half the markers are greyed out would be a bug, not a
    /// rule.
    /// </summary>
    bool Shut(int index)
    {
        TrailService trails = TrailService.Instance;
        TrailService.Trail trail = trails != null ? trails.Selected : null;

        // Blank means a row written before the column existed, which the rest of
        // the app treats as guided.
        bool guided = trail == null || string.IsNullOrEmpty(trail.mode) || trail.mode == "guide";

        return guided && index > FirstAhead();
    }

    /// <summary>
    /// Puts the "you are here" mark on the line.
    ///
    /// Halfway between the last stop found and the one ahead, because that is
    /// where somebody walking actually is - and above the first stop before
    /// anything has been found, since the walk has not started.
    /// </summary>
    void PlaceHere()
    {
        if (herePip == null)
            return;

        bool any = _places.Length > 0;
        herePip.gameObject.SetActive(any);

        if (!any)
            return;

        if (hereLabel != null)
            hereLabel.text = AppStrings.Get("strip.here");

        int ahead = FirstAhead();

        herePip.anchoredPosition = ahead > 0
            ? (_places[ahead - 1] + _places[ahead]) * 0.5f
            : _places[0] + new Vector2(0f, Spacing * 0.5f);
    }

    /// <summary>
    /// The first marker not yet found, which is where "you are here" goes and
    /// what the line opens at. The end of the line once everything is found.
    /// </summary>
    int FirstAhead()
    {
        for (int i = 0; i < _ordered.Count; i++)
        {
            if (!DiscoveryTracker.IsDiscovered(_ordered[i]))
                return i;
        }

        return Mathf.Max(0, _ordered.Count - 1);
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

            _nodes[i].Bind(captured, i + 1, DiscoveryTracker.IsDiscovered(captured), Shut(i),
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

    void ApplyZoom()
    {
        for (int i = 0; i < _nodes.Count && i < _places.Length; i++)
        {
            if (_nodes[i] != null)
            {
                ((RectTransform)_nodes[i].transform).anchoredPosition =
                    _places[i] - new Vector2(KnobOffsetX, 0f);
            }
        }

        for (int i = 0; i < _edges.Count; i++)
        {
            if (_edges[i] == null || i + 1 >= _places.Length)
                continue;

            Vector2 from = _places[i];
            Vector2 to = _places[i + 1];
            Vector2 along = to - from;

            _edges[i].anchoredPosition = (from + to) * 0.5f;
            _edges[i].sizeDelta = new Vector2(along.magnitude, 7f);
            _edges[i].localRotation =
                Quaternion.Euler(0f, 0f, Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg);
        }
    }

    /// <summary>Back to where the walker is on the line. Wired to a button.</summary>
    public void Fit()
    {
        if (content != null && _places.Length > 0)
            content.anchoredPosition = OpenedAt();
    }

    /// <summary>
    /// Where the line sits when it opens, and where Fit puts it back.
    ///
    /// The stop goes a third of the way down rather than in the middle: what
    /// somebody wants to see is the one ahead and the two after it, and centring
    /// it spends half the card on the walk already done.
    /// </summary>
    Vector2 OpenedAt()
    {
        if (_places.Length == 0)
            return Vector2.zero;

        float height = viewport != null ? viewport.rect.height : 900f;

        // Plus, not minus. Raising content.y lifts the line, which brings the
        // stop UP the card; subtracting pushed it to the bottom edge.
        return new Vector2(0f, -_places[FirstAhead()].y + height * 0.30f);
    }
}
