using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The disc in the corner of the camera view: every marker around the reader as a
/// dot, and a wedge for the part of the world the phone is actually looking at.
///
/// It replaces the two edge tabs. Those could name one marker to the left and one
/// to the right and said nothing about anything else, nothing about distance, and
/// nothing at all about what was directly behind. This shows bearing and distance
/// for everything at once, in a corner, and it means the reader does not have to
/// leave the camera to answer "what is around me".
///
/// Heading up, not north up. On a screen whose entire subject is where the phone
/// is pointing, a north-up radar would need reading twice.
/// </summary>
public class CameraRadar : MonoBehaviour
{
    [Header("References")]
    public UserLocationService locationService;

    [Tooltip("Dots are created under here, one per marker in range.")]
    public RectTransform dotParent;

    [Tooltip("The slice of the world the camera can see. Fixed, pointing up: the " +
             "dots move around it rather than it moving around them.")]
    public RectTransform wedge;

    [Header("Shape")]
    [Tooltip("Distance from the centre of the disc to its edge, in canvas units.")]
    public float radius = 132f;

    public float dotSize = 22f;

    [Header("Range")]
    [Tooltip("A marker at this distance sits on the rim. Anything further is held " +
             "at the rim rather than dropped, so the reader is never told that a " +
             "marker they can see on the map does not exist.")]
    public double maxRangeMetres = 500d;

    readonly Dictionary<BuildingData, RectTransform> _dots =
        new Dictionary<BuildingData, RectTransform>();

    readonly List<BuildingData> _spent = new List<BuildingData>();

    float _lastHeading = float.NaN;
    double _lastLat, _lastLon;
    bool _dirty = true;

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (locationService != null)
            locationService.OnPoseUpdated += MarkDirty;

        BuildingDatabase.OnBuildingsChanged += Rebuild;
        ThemeManager.OnThemeChanged += MarkDirty;

        Rebuild();
    }

    void OnDisable()
    {
        if (locationService != null)
            locationService.OnPoseUpdated -= MarkDirty;

        BuildingDatabase.OnBuildingsChanged -= Rebuild;
        ThemeManager.OnThemeChanged -= MarkDirty;
    }

    void MarkDirty()
    {
        _dirty = true;
    }

    void Update()
    {
        if (locationService == null)
            return;

        // Turning is the whole point of this thing, so it follows the compass
        // rather than a timer - but only past the angle a hand can hold still.
        // Below half a degree it would redraw the canvas for nothing.
        float heading = locationService.DeviceHeading;

        if (float.IsNaN(_lastHeading) || Mathf.Abs(Mathf.DeltaAngle(_lastHeading, heading)) > 0.4f)
        {
            _lastHeading = heading;
            _dirty = true;
        }

        if (!_dirty)
            return;

        _dirty = false;
        LayOut();
    }

    /// <summary>Creates and destroys dots to match the database.</summary>
    public void Rebuild()
    {
        if (dotParent == null)
            return;

        var all = BuildingDatabase.AllBuildings;

        _spent.Clear();
        foreach (KeyValuePair<BuildingData, RectTransform> pair in _dots)
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
            RectTransform spent = _dots[_spent[i]];
            _dots.Remove(_spent[i]);

            if (spent != null)
                Destroy(spent.gameObject);
        }

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] == null || _dots.ContainsKey(all[i]))
                continue;

            _dots.Add(all[i], CreateDot(all[i]));
        }

        MarkDirty();
    }

    /// <summary>
    /// A coloured disc inside a white one. The ring is what keeps a marker whose
    /// colour is nearly black - and one of them is - visible on a dark disc.
    /// </summary>
    RectTransform CreateDot(BuildingData marker)
    {
        GameObject go = new GameObject("Dot - " + marker.DisplayName, typeof(RectTransform));
        go.layer = dotParent.gameObject.layer;
        go.transform.SetParent(dotParent, false);

        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(dotSize, dotSize);

        Color colour = MarkerPalette.Resolve(marker);

        Image ring = go.AddComponent<Image>();
        ring.sprite = _circle;
        ring.color = MarkerPalette.RingFor(colour);
        ring.raycastTarget = false;

        GameObject inner = new GameObject("Fill", typeof(RectTransform));
        inner.layer = go.layer;
        inner.transform.SetParent(rt, false);

        RectTransform innerRect = (RectTransform)inner.transform;
        innerRect.anchorMin = new Vector2(0.5f, 0.5f);
        innerRect.anchorMax = new Vector2(0.5f, 0.5f);
        innerRect.pivot = new Vector2(0.5f, 0.5f);
        innerRect.sizeDelta = new Vector2(dotSize - 7f, dotSize - 7f);

        Image fill = inner.AddComponent<Image>();
        fill.sprite = _circle;
        fill.color = colour;
        fill.raycastTarget = false;

        return rt;
    }

    /// <summary>
    /// The circle every dot is drawn with. Supplied by the builder rather than
    /// loaded here, because the sprite is generated in the editor.
    /// </summary>
    public Sprite circleSprite
    {
        get { return _circle; }
        set { _circle = value; }
    }

    [SerializeField] Sprite _circle;

    void LayOut()
    {
        bool known = locationService != null && locationService.HasValidPose;

        // Without a compass the dots would be placed at angles that mean nothing.
        // Better to show an empty disc than a confident wrong one.
        bool usable = known && locationService.HasHeading;

        if (wedge != null && wedge.gameObject.activeSelf != usable)
            wedge.gameObject.SetActive(usable);

        foreach (KeyValuePair<BuildingData, RectTransform> pair in _dots)
        {
            RectTransform dot = pair.Value;
            if (dot == null)
                continue;

            double distance = usable ? locationService.DistanceMetresTo(pair.Key) : -1d;

            if (distance < 0d)
            {
                if (dot.gameObject.activeSelf)
                    dot.gameObject.SetActive(false);

                continue;
            }

            if (!dot.gameObject.activeSelf)
                dot.gameObject.SetActive(true);

            double bearing = GeoMath.BearingDegrees(
                locationService.Latitude, locationService.Longitude,
                pair.Key.latitude, pair.Key.longitude);

            float relative = Mathf.DeltaAngle(locationService.DeviceHeading, (float)bearing);
            float radians = relative * Mathf.Deg2Rad;

            // Held at the rim rather than dropped past the range, so a marker the
            // map is showing never silently disappears from the radar.
            float reach = Mathf.Min((float)(distance / maxRangeMetres), 1f) * (radius - dotSize * 0.5f);

            Vector2 at = new Vector2(Mathf.Sin(radians) * reach, Mathf.Cos(radians) * reach);

            if (dot.anchoredPosition != at)
                dot.anchoredPosition = at;
        }
    }

    /// <summary>Re-reads every dot's colour, e.g. after a theme change.</summary>
    public void Repaint()
    {
        foreach (KeyValuePair<BuildingData, RectTransform> pair in _dots)
        {
            if (pair.Value == null)
                continue;

            Color colour = MarkerPalette.Resolve(pair.Key);

            Image ring = pair.Value.GetComponent<Image>();
            if (ring != null)
                ring.color = MarkerPalette.RingFor(colour);

            Transform fill = pair.Value.Find("Fill");
            if (fill != null)
            {
                Image image = fill.GetComponent<Image>();
                if (image != null)
                    image.color = colour;
            }
        }
    }
}
