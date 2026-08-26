using Google.XR.ARCoreExtensions;
using TMPro;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// On-screen readout of ARCore's Geospatial state for field testing. Without this
/// there is no way to tell, standing outside, whether a missing marker means Earth
/// tracking hasn't localized, the anchor is in the wrong place, or the marker is
/// simply behind you.
///
/// Drop this on an empty GameObject. It builds its own canvas at runtime, so there
/// is nothing to wire up. Disable or delete it for release builds.
/// </summary>
public class GeospatialDebugOverlay : MonoBehaviour
{
    [Header("Optional - found automatically if left empty")]
    public AREarthManager earthManager;
    public TappableObject marker;

    [Header("Display")]
    [Tooltip("Font size in the overlay's own 1080x1920 reference space.")]
    public float fontSize = 30f;

    TextMeshProUGUI _output;

    void Awake()
    {
        if (earthManager == null)
            earthManager = FindFirstObjectByType<AREarthManager>();

        if (marker == null)
            marker = FindFirstObjectByType<TappableObject>();

        _output = BuildOverlay();
    }

    void Update()
    {
        // AREarthManager reads straight through ARCoreExtensions' static instance,
        // which isn't there until an ARCore session exists. In the Editor, or during
        // the first frames on device, that throws rather than returning a state.
        try
        {
            _output.text = BuildReport();
        }
        catch (System.Exception exception)
        {
            _output.text = "<color=#ffa657>Geospatial session not ready</color>\n" +
                           "(" + exception.GetType().Name + ")\n\n" +
                           "Expected in the Editor - there is no\nAR session outside a device build.";
        }
    }

    string BuildReport()
    {
        if (earthManager == null)
            return "<color=#ff6b6b>No AREarthManager in the scene.</color>";

        EarthState earthState = earthManager.EarthState;
        TrackingState trackingState = earthManager.EarthTrackingState;

        string report =
            $"EARTH   {Colour(earthState == EarthState.Enabled)}{earthState}</color>\n" +
            $"TRACK   {Colour(trackingState == TrackingState.Tracking)}{trackingState}</color>\n";

        if (trackingState != TrackingState.Tracking)
        {
            return report +
                   "\nWaiting for localization.\nPan the phone slowly across\n" +
                   "buildings and landmarks.\nVPS needs daylight and a\nclear view of facades.";
        }

        GeospatialPose pose = earthManager.CameraGeospatialPose;

        report +=
            $"\nYOU     {pose.Latitude:F7}\n        {pose.Longitude:F7}\n" +
            $"ALT     {pose.Altitude:F1} m\n" +
            $"ACC     H {pose.HorizontalAccuracy:F1} m  " +
            $"V {pose.VerticalAccuracy:F1} m\n" +
            $"        yaw {pose.OrientationYawAccuracy:F0}°\n";

        report += BuildMarkerReport(pose);
        return report;
    }

    string BuildMarkerReport(GeospatialPose pose)
    {
        if (marker == null)
            return "\n<color=#ff6b6b>No TappableObject in the scene.</color>";

        string report = $"\nMARKER  {marker.name}\n";

        if (marker.buildingData != null)
        {
            double distance = GeoMath.DistanceMetres(
                pose.Latitude, pose.Longitude,
                marker.buildingData.latitude, marker.buildingData.longitude);

            report += $"        {marker.buildingData.latitude:F7}\n" +
                      $"        {marker.buildingData.longitude:F7}\n" +
                      $"GEO     {distance:F1} m away\n";
        }

        Camera cam = Camera.main;
        if (cam == null)
            return report + "<color=#ff6b6b>No MainCamera.</color>";

        float worldDistance = Vector3.Distance(cam.transform.position, marker.transform.position);
        Vector3 viewport = cam.WorldToViewportPoint(marker.transform.position);

        bool onScreen = viewport.z > 0f &&
                        viewport.x >= 0f && viewport.x <= 1f &&
                        viewport.y >= 0f && viewport.y <= 1f;

        // A world distance in the thousands means the anchor resolved somewhere far
        // from you - almost always wrong coordinates rather than a tracking problem.
        report += $"WORLD   {worldDistance:F1} m away\n" +
                  $"ONSCREEN {Colour(onScreen)}{(onScreen ? "YES" : (viewport.z > 0f ? "off to the side" : "BEHIND YOU"))}</color>";

        return report;
    }

    static string Colour(bool good)
    {
        return good ? "<color=#7ee787>" : "<color=#ffa657>";
    }

    TextMeshProUGUI BuildOverlay()
    {
        GameObject canvasObject = new GameObject("GeospatialDebugCanvas");
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 999; // always on top of the app's own UI

        UnityEngine.UI.CanvasScaler scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0.5f;

        GameObject textObject = new GameObject("Readout", typeof(RectTransform));
        textObject.transform.SetParent(canvasObject.transform, false);

        RectTransform rect = (RectTransform)textObject.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(-40f, 700f);
        rect.anchoredPosition = new Vector2(0f, -60f);

        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.fontSize = fontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.richText = true;
        text.raycastTarget = false; // never steal taps from the app underneath

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
    }
}
