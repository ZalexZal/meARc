using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Lets the app draw faster than the camera delivers frames.
///
/// AR Foundation matches the render loop to the camera by default, and ARCore
/// delivers thirty frames a second. That is right for the AR view - drawing a
/// marker twice against one camera image gains nothing - and wrong for everything
/// else, because it also caps the map, the sheets, and every animation in the app
/// at thirty. A sheet sliding at 30fps on a phone whose display runs at 90 does
/// not read as a slow animation; it reads as a stutter.
///
/// So: unmatched, with a target of sixty. The AR session still consumes camera
/// frames at whatever rate ARCore provides them.
/// </summary>
public class FrameRate : MonoBehaviour
{
    [Tooltip("Left at 60 rather than pushed higher. A walking reader with a phone " +
             "out is already spending battery on GPS, the camera and the radio.")]
    public int target = 60;

    public ARSession session;

    void Start()
    {
        if (session == null)
            session = FindAnyObjectByType<ARSession>();

        if (session != null)
            session.matchFrameRateRequested = false;

        // vSync overrides targetFrameRate when it is on, so the target would be
        // quietly ignored.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = target;
    }
}
