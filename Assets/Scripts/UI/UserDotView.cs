using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The reader's own dot on the map: a soft pulse that says the position is live,
/// and a faint cone pointing the way the phone faces.
///
/// The pulse is the one thing in Daylight that moves while nothing is happening,
/// and it earns that because it is information: a dot that breathes is a fix
/// that is still arriving. The cone shows only when the compass has a heading,
/// and turns with it on a north-up map.
/// </summary>
public class UserDotView : MonoBehaviour
{
    public UserLocationService locationService;
    public RectTransform pulse;
    public Image pulseImage;
    public RectTransform beam;

    [Tooltip("Seconds for one breath out.")]
    public float period = 2.4f;

    float _t;

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;
    }

    void Update()
    {
        _t += Time.unscaledDeltaTime;
        float k = (_t % period) / period;

        if (pulse != null)
        {
            float s = 1f + k * 1.6f;
            pulse.localScale = new Vector3(s, s, 1f);
        }

        if (pulseImage != null)
        {
            Color c = ThemeManager.Get(ThemeRole.Accent);
            c.a = 0.35f * (1f - k);
            pulseImage.color = c;
        }

        if (beam == null)
            return;

        bool show = locationService != null && locationService.HasHeading;
        if (beam.gameObject.activeSelf != show)
            beam.gameObject.SetActive(show);

        if (show)
            beam.localRotation = Quaternion.Euler(0f, 0f, -locationService.DeviceHeading);
    }
}
