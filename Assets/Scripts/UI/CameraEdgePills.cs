using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The two tabs pinned to the sides of the camera view, naming the nearest marker
/// that is off to the left and the nearest that is off to the right.
///
/// A camera view is a window about sixty degrees wide onto a world that is three
/// hundred and sixty. Without these, a reader standing among four markers sees an
/// empty street and concludes the app is broken. Tapping one selects that marker,
/// which is also the answer to "how do I look at the one behind me".
///
/// Driven by compass bearing rather than by where the anchors ended up, so the
/// pills work before any anchor has resolved - which is most of the time indoors,
/// and all of the time on a first launch.
/// </summary>
public class CameraEdgePills : MonoBehaviour
{
    [System.Serializable]
    public class Pill
    {
        public GameObject root;
        public Button button;
        public Image dot;
        public Image ring;
        public TextMeshProUGUI label;
        public TextMeshProUGUI distance;
    }

    [Header("References")]
    public UserLocationService locationService;

    [Header("Pills")]
    public Pill left;
    public Pill right;

    [Header("Behaviour")]
    [Tooltip("Markers within this angle of straight ahead are treated as visible in " +
             "the feed, so they get no pill. Roughly a phone camera's field of view.")]
    public float visibleHalfAngle = 32f;

    [Tooltip("Beyond this there is no point telling someone to turn round.")]
    public double maxDistanceMetres = 600d;

    /// <summary>Raised when a pill is tapped, with the marker it names.</summary>
    public System.Action<BuildingData> onSelected;

    BuildingData _left;
    BuildingData _right;
    float _timer;

    void Awake()
    {
        if (left != null && left.button != null)
            left.button.onClick.AddListener(SelectLeft);

        if (right != null && right.button != null)
            right.button.onClick.AddListener(SelectRight);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        Refresh();
    }

    void Update()
    {
        // Twice a second. The reader turns far faster than that, but the pills only
        // change when a marker crosses in or out of view, and redrawing text every
        // frame is how a battery disappears.
        _timer += Time.unscaledDeltaTime;
        if (_timer < 0.5f)
            return;

        _timer = 0f;
        Refresh();
    }

    void Refresh()
    {
        _left = null;
        _right = null;

        if (locationService == null)
            locationService = UserLocationService.Instance;

        bool usable = locationService != null &&
                      locationService.HasValidPose &&
                      locationService.HasHeading;

        if (usable)
            Choose();

        Apply(left, _left);
        Apply(right, _right);
    }

    void Choose()
    {
        double bestLeft = double.MaxValue;
        double bestRight = double.MaxValue;

        IReadOnlyList<BuildingData> all = BuildingDatabase.AllBuildings;

        for (int i = 0; i < all.Count; i++)
        {
            BuildingData building = all[i];
            if (building == null)
                continue;

            double distance = locationService.DistanceMetresTo(building);
            if (distance < 0d || distance > maxDistanceMetres)
                continue;

            double bearing = GeoMath.BearingDegrees(
                locationService.Latitude, locationService.Longitude,
                building.latitude, building.longitude);

            float relative = Mathf.DeltaAngle(locationService.DeviceHeading, (float)bearing);

            // Already in the feed - the marker itself is the label.
            if (Mathf.Abs(relative) <= visibleHalfAngle)
                continue;

            if (relative < 0f && distance < bestLeft)
            {
                bestLeft = distance;
                _left = building;
            }
            else if (relative > 0f && distance < bestRight)
            {
                bestRight = distance;
                _right = building;
            }
        }
    }

    void Apply(Pill pill, BuildingData building)
    {
        if (pill == null || pill.root == null)
            return;

        pill.root.SetActive(building != null);

        if (building == null)
            return;

        Color colour = MarkerPalette.Resolve(building);

        if (pill.dot != null)
            pill.dot.color = colour;

        if (pill.ring != null)
            pill.ring.color = MarkerPalette.RingFor(colour);

        if (pill.label != null)
            pill.label.text = building.DisplayName;

        if (pill.distance != null)
        {
            pill.distance.text = AppStrings.FormatDistance(
                locationService.DistanceMetresTo(building),
                locationService.IsApproximate);
        }
    }

    void SelectLeft()
    {
        Raise(_left);
    }

    void SelectRight()
    {
        Raise(_right);
    }

    void Raise(BuildingData building)
    {
        if (building != null && onSelected != null)
            onSelected(building);
    }
}
