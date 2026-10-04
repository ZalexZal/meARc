using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A pill at the left or right edge of the camera when the next stop is behind
/// the reader's shoulder: "‹ Museum · 180 m". It says which way to turn, which is
/// the one thing a camera pointed the wrong way cannot show.
///
/// Off while the stop is roughly in front, while there is no compass, and once
/// the reader is standing at it. A tap opens the stop.
/// </summary>
public class EdgeIndicator : MonoBehaviour
{
    public UserLocationService locationService;
    public InfoPanelController infoPanel;

    public Button left;
    public TextMeshProUGUI leftLabel;
    public Button right;
    public TextMeshProUGUI rightLabel;

    [Tooltip("Degrees either side of straight ahead that count as in view.")]
    public float halfView = 32f;

    float _timer;
    BuildingData _stop;

    void Awake()
    {
        if (left != null) left.onClick.AddListener(Open);
        if (right != null) right.onClick.AddListener(Open);
        Show(0);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;
    }

    void Open()
    {
        if (_stop != null && infoPanel != null)
            infoPanel.ShowPanel(_stop);
    }

    void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < 0.2f)
            return;

        _timer = 0f;

        _stop = NextStopCard.Current;
        UserLocationService at = locationService;

        if (_stop == null || at == null || !at.HasValidPose || !at.HasHeading ||
            CaptureMode.IsOpen || HiddenMarkers.IsHidden(_stop))
        {
            Show(0);
            return;
        }

        double metres = at.DistanceMetresTo(_stop);
        if (metres < 0d || metres <= NextStopCard.NearMetres(_stop))
        {
            Show(0);
            return;
        }

        double bearing = GeoMath.BearingDegrees(at.Latitude, at.Longitude, _stop.latitude, _stop.longitude);
        float turn = Mathf.DeltaAngle(at.DeviceHeading, (float)bearing);

        int side = turn < -halfView ? -1 : turn > halfView ? 1 : 0;
        Show(side);

        if (side == 0)
            return;

        string text = _stop.DisplayName + " · " + AppStrings.FormatDistance(metres, at.IsApproximate);
        TextMeshProUGUI label = side < 0 ? leftLabel : rightLabel;
        if (label != null && label.text != text)
            label.text = text;
    }

    void Show(int side)
    {
        if (left != null && left.gameObject.activeSelf != (side < 0))
            left.gameObject.SetActive(side < 0);

        if (right != null && right.gameObject.activeSelf != (side > 0))
            right.gameObject.SetActive(side > 0);
    }
}
