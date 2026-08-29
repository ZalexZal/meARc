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

    public void SetTarget(BuildingData building)
    {
        _target = building;
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

        if (arrow != null)
            arrow.gameObject.SetActive(known && !arrived);

        if (arrivedIcon != null)
            arrivedIcon.SetActive(arrived);

        if (!known || arrived || arrow == null)
            return;

        double bearing = GeoMath.BearingDegrees(
            locationService.Latitude, locationService.Longitude,
            _target.latitude, _target.longitude);

        if (frame == Frame.DeviceRelative)
            bearing -= locationService.DeviceHeading;

        // Negated because a compass bearing runs clockwise and Unity's Z rotation
        // runs anticlockwise.
        arrow.localRotation = Quaternion.Euler(0f, 0f, -(float)bearing);

        if (tinted != null)
            tinted.color = ThemeManager.Get(ThemeRole.Accent);
    }
}
