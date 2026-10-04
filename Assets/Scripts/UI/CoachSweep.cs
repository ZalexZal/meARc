using UnityEngine;

/// <summary>
/// The phone in the camera's coach, swinging slowly left and right: "point it
/// at the buildings around you", shown rather than only said.
/// </summary>
public class CoachSweep : MonoBehaviour
{
    public float period = 2.4f;
    public float reach = 83f;
    public float tilt = 8f;

    RectTransform _rect;

    void OnEnable()
    {
        _rect = (RectTransform)transform;
    }

    void Update()
    {
        // Ease in and out at each end, as a hand would.
        float t = (Time.unscaledTime % period) / period;
        float k = -Mathf.Cos(t * Mathf.PI * 2f);
        _rect.anchoredPosition = new Vector2(k * reach, _rect.anchoredPosition.y);
        _rect.localRotation = Quaternion.Euler(0f, 0f, -k * tilt);
    }
}
