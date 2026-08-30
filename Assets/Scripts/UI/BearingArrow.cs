using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// An arrow that points at a building.
///
/// Every distance in this app is shown with one of these, because "180 m" on its
/// own is half an answer - it tells the reader how far but not which way, and on a
/// street that is the half they need.
/// </summary>
public class BearingArrow : MonoBehaviour
{
    public enum Frame
    {
        /// <summary>The map is drawn north up, so a true bearing is the angle.</summary>
        NorthUp,

        /// <summary>Over the camera, where the angle is relative to where the phone points.</summary>
        DeviceRelative
    }

    [Header("References")]
    public UserLocationService locationService;

    [Tooltip("Rotated to point at the target. Its artwork must point up at zero rotation.")]
    public RectTransform arrow;

    [Tooltip("Hidden along with the arrow when there is nothing to point at.")]
    public Image tinted;

    [Header("Behaviour")]
    public Frame frame = Frame.NorthUp;

    [Tooltip("Below this distance the arrow is replaced by a filled dot: at a few " +
             "paces a direction is noise, and pointing confidently at nothing is worse " +
             "than admitting you have arrived.")]
    public float arrivedMetres = 15f;

    public GameObject arrivedIcon;

    BuildingData _target;

    // Last values written. An unchanged colour still marks the graphic dirty,
    // which rebuilds the canvas - so nothing is written twice.
    float _lastAngle = float.NaN;
    Color _lastColour;
    bool _lastArrowOn;
    bool _lastArrivedOn;
    float _timer;

    public void SetTarget(BuildingData building)
    {
        if (_target == building)
            return;

        _target = building;
        _lastAngle = float.NaN;
        Refresh();
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        Refresh();
    }

    void Update()
    {
        // Four times a second. A walking reader turns far slower than sixty hertz,
        // and the arrow is redrawn on a shared canvas.
        _timer += Time.unscaledDeltaTime;
        if (_timer < 0.25f)
            return;

        _timer = 0f;
        Refresh();
    }

    void Refresh()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        bool known = _target != null &&
                     locationService != null &&
                     locationService.HasValidPose;

        // Over the camera the angle is meaningless without knowing where the phone
        // faces, so the arrow stands down rather than pointing somewhere plausible.
        if (known && frame == Frame.DeviceRelative && !locationService.HasHeading)
            known = false;

        double distance = known ? locationService.DistanceMetresTo(_target) : -1d;
        bool arrived = known && distance >= 0d && distance < arrivedMetres;

        bool arrowOn = known && !arrived;

        if (arrow != null && arrowOn != _lastArrowOn)
        {
            _lastArrowOn = arrowOn;
            arrow.gameObject.SetActive(arrowOn);
        }

        if (arrivedIcon != null && arrived != _lastArrivedOn)
        {
            _lastArrivedOn = arrived;
            arrivedIcon.SetActive(arrived);
        }

        if (!known || arrived || arrow == null)
            return;

        double bearing = GeoMath.BearingDegrees(
            locationService.Latitude, locationService.Longitude,
            _target.latitude, _target.longitude);

        if (frame == Frame.DeviceRelative)
            bearing -= locationService.DeviceHeading;

        // Negated because a compass bearing runs clockwise and Unity's Z rotation
        // runs anticlockwise.
        float angle = -(float)bearing;

        // Half a degree is below what anyone can see and above what the compass
        // can hold still, so smaller changes are not worth a redraw.
        if (float.IsNaN(_lastAngle) || Mathf.Abs(Mathf.DeltaAngle(_lastAngle, angle)) > 0.5f)
        {
            _lastAngle = angle;
            arrow.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        if (tinted == null)
            return;

        Color colour = ThemeManager.Get(ThemeRole.Accent);
        if (colour != _lastColour)
        {
            _lastColour = colour;
            tinted.color = colour;
        }
    }
}
