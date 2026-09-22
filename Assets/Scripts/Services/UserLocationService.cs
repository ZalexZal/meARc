using System;
using System.Collections;
using System.Collections.Generic;
using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

using UnityEngine.XR.ARSubsystems;

public enum VpsStatus
{
    Unavailable,
    Poor,
    Fair,
    Good
}

/// <summary>Where the current position came from. They are not interchangeable.</summary>
public enum PositionSource
{
    None,
    Gps,
    Geospatial
}

/// <summary>
/// Wraps AREarthManager so no UI script ever touches an AR API directly, and supplies
/// a coarse position when Earth tracking is not available.
///
/// Two tiers, deliberately:
///
/// Geospatial - fused GNSS, IMU, compass, visual-inertial odometry and VPS. Accurate
/// to under a metre once VPS localizes, and the only source with a trustworthy
/// heading. Marker placement uses this and nothing else.
///
/// GPS - Unity's own location service, accurate to five or twenty metres and carrying
/// no heading at all. Useless for placing a marker: at a hundred metres a fifteen
/// degree heading error puts one twenty-seven metres sideways, a whole building off.
/// It is fine for "which buildings are near me" and "how far is that", which is the
/// larger part of the app and previously went blank indoors or after dark.
/// </summary>
public class UserLocationService : MonoBehaviour
{
    public static UserLocationService Instance { get; private set; }

    [Header("References")]
    [Tooltip("The AREarthManager on the XR Origin.")]
    public AREarthManager earthManager;

    [Header("Polling")]
    [Tooltip("Seconds between position reads.")]
    public float pollInterval = 1f;

    [Header("Accuracy thresholds (metres)")]
    [Tooltip("At or under this horizontal accuracy, calibration reads as Good.")]
    public double goodThreshold = 1.0;

    [Tooltip("At or under this horizontal accuracy, calibration reads as Fair.")]
    public double fairThreshold = 5.0;

    [Header("GPS fallback")]
    [Tooltip("Fall back to the device's own location when Earth tracking isn't " +
             "available, so lists and distances keep working indoors and at night.")]
    public bool useGpsFallback = true;

    [Tooltip("Metres of accuracy asked of the OS. Tighter costs more battery for " +
             "precision this tier doesn't need.")]
    public float gpsDesiredAccuracy = 10f;

    [Tooltip("Seconds to wait for the OS location service to start before giving up.")]
    public float gpsStartTimeout = 20f;

    /// <summary>Raised only when the status bucket actually changes.</summary>
    public event Action<VpsStatus> OnStatusChanged;

    /// <summary>Raised on every poll, for live distance refreshes.</summary>
    public event Action OnPoseUpdated;

    /// <summary>True when there is any usable position, precise or coarse.</summary>
    public bool HasValidPose { get; private set; }

    /// <summary>
    /// True only for a tracked Geospatial pose. This is the gate for anything placed
    /// in the world - a marker positioned from a GPS fix would sit on the wrong
    /// building, which is worse than no marker at all.
    /// </summary>
    public bool HasPrecisePose { get { return Source == PositionSource.Geospatial; } }

    /// <summary>True when distances derived from this position should be hedged.</summary>
    public bool IsApproximate { get { return Source == PositionSource.Gps; } }

    public PositionSource Source { get; private set; } = PositionSource.None;
    public double Latitude { get; private set; }
    public double Longitude { get; private set; }
    public double HorizontalAccuracy { get; private set; }
    public VpsStatus Status { get; private set; } = VpsStatus.Unavailable;

    /// <summary>
    /// Which way the phone is pointing, in degrees clockwise from true north, or
    /// 0 when the device has no usable compass.
    ///
    /// Read from the magnetometer rather than from the AR session: the arrows that
    /// use it have to keep pointing while the reader is looking at the map with the
    /// phone flat, which is exactly when the camera has nothing to localize against.
    /// </summary>
    public float DeviceHeading { get; private set; }

    public bool HasHeading { get; private set; }

    /// <summary>
    /// Whether Google has VPS coverage where the reader is standing. Checked as soon
    /// as any position is known - including a GPS one - so the Ready State can say
    /// "there is no visual positioning here" instead of coaching someone forever to
    /// point at facades that will never help.
    /// </summary>
    public VpsAvailability CoverageHere { get; private set; } = VpsAvailability.Unknown;

    float _timer;
    bool _coverageChecked;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        // Not on a first run. The permission is asked when the app is first
        // usable - the demo placing its stops - rather than cold over a
        // welcome screen that has not yet said what the app is.
        if (AndroidPermissions.Has(FineLocation) || PlayerPrefs.GetInt(DoorFlow.WelcomeSeenKey, 0) == 1)
            Begin();

        // Harmless when there is no magnetometer - trueHeading simply stays at 0
        // and HasHeading never turns on, which the arrows check before drawing.
        Input.compass.enabled = true;
    }

    void OnDisable()
    {
        if (Input.location.status == LocationServiceStatus.Running)
            Input.location.Stop();
    }

    void Update()
    {
        ReadHeading();

        _timer += Time.unscaledDeltaTime;
        if (_timer < pollInterval)
            return;

        _timer = 0f;
        Poll();
    }

    /// <summary>
    /// Smoothed, because a raw magnetometer reading jitters by several degrees a
    /// frame and an arrow that shivers reads as broken rather than as precise.
    /// </summary>
    void ReadHeading()
    {
        if (!Input.compass.enabled || Input.compass.timestamp <= 0d)
            return;

        float heading = Input.compass.trueHeading;

        if (!HasHeading)
        {
            DeviceHeading = heading;
            HasHeading = true;
            return;
        }

        // Interpolated the short way round, so 359 to 1 degrees does not spin the
        // arrow the whole way back.
        float delta = Mathf.DeltaAngle(DeviceHeading, heading);
        DeviceHeading = Mathf.Repeat(DeviceHeading + delta * Mathf.Clamp01(Time.deltaTime * 8f), 360f);
    }

    // ------------------------------------------------------------------ polling

    void Poll()
    {
        VpsStatus previous = Status;

        // Geospatial first, always. It is strictly better than GPS when available -
        // GPS is a floor, not a supplement.
        if (TryReadGeospatial())
        {
            Source = PositionSource.Geospatial;
            HasValidPose = true;

            if (HorizontalAccuracy <= goodThreshold)
                Status = VpsStatus.Good;
            else if (HorizontalAccuracy <= fairThreshold)
                Status = VpsStatus.Fair;
            else
                Status = VpsStatus.Poor;
        }
        else if (TryReadGps())
        {
            Source = PositionSource.Gps;
            HasValidPose = true;

            // Deliberately Unavailable rather than Poor. Visual positioning genuinely
            // is unavailable, Start stays disabled, and the existing copy - "markers
            // use GPS only and may be well off" - is already exactly true.
            Status = VpsStatus.Unavailable;
        }
        else
        {
            Source = PositionSource.None;
            HasValidPose = false;
            Status = VpsStatus.Unavailable;
        }

        if (HasValidPose && !_coverageChecked)
        {
            _coverageChecked = true;
            StartCoroutine(CheckCoverage(Latitude, Longitude));
        }

        if (Status != previous && OnStatusChanged != null)
            OnStatusChanged(Status);

        if (OnPoseUpdated != null)
            OnPoseUpdated();
    }

    bool TryReadGeospatial()
    {
        if (earthManager == null)
            return false;

        // AREarthManager reads through ARCoreExtensions' static instance, which
        // isn't there until an ARCore session exists. In the Editor, or during the
        // first frames on device, that throws rather than returning a state.
        try
        {
            if (earthManager.EarthState != EarthState.Enabled)
                return false;

            if (earthManager.EarthTrackingState != TrackingState.Tracking)
                return false;

            GeospatialPose pose = earthManager.CameraGeospatialPose;
            Latitude = pose.Latitude;
            Longitude = pose.Longitude;
            HorizontalAccuracy = pose.HorizontalAccuracy;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    bool TryReadGps()
    {
        if (!useGpsFallback || Input.location.status != LocationServiceStatus.Running)
            return false;

        LocationInfo data = Input.location.lastData;

        Latitude = data.latitude;
        Longitude = data.longitude;
        HorizontalAccuracy = data.horizontalAccuracy;
        return true;
    }

    bool _begun;

    /// <summary>True while the permission dialog is up.</summary>
    public bool Asking { get; private set; }

    /// <summary>Starts the position, asking for the permission if it has to. Safe to call again.</summary>
    public void Begin()
    {
        if (_begun || !useGpsFallback)
            return;

        _begun = true;
        StartCoroutine(StartGps());
    }

    /// <summary>Named here so the string is not repeated at the call site.</summary>
    const string FineLocation = "android.permission.ACCESS_FINE_LOCATION";

    IEnumerator StartGps()
    {
        // A few frames of daylight first.
        //
        // Unity used to request every declared permission during startup, and the
        // app could not survive the dialog: it arrived while the window was still
        // settling, the activity paused and never resumed. That request is off now
        // (see AndroidPermissionsManifest), so this is the one that asks - and it
        // waits until there is a drawn, running app behind the dialog.
        yield return null;
        yield return null;
        yield return new WaitForSecondsRealtime(0.5f);

        // Through AndroidPermissions, which does not register a native callback.
        // Unity's callback delivery is what crashes: answering any dialog killed
        // the app with a SIGSEGV at address zero inside Unity's own result
        // handling, whoever asked and whenever.
        bool granted = AndroidPermissions.Has(FineLocation);

        if (!granted)
        {
            Asking = true;
            yield return AndroidPermissions.Ask(FineLocation, answer => granted = answer);
            Asking = false;
        }

        // Refused. The map says where it does not know rather than pretending, and
        // everything that does not need a position still works.
        if (!granted)
        {
            Debug.LogWarning("[Location] No location permission; running without a position.");
            yield break;
        }


        if (!Input.location.isEnabledByUser)
            yield break;

        Input.location.Start(gpsDesiredAccuracy, gpsDesiredAccuracy);

        float waited = 0f;
        while (Input.location.status == LocationServiceStatus.Initializing &&
               waited < gpsStartTimeout)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (Input.location.status != LocationServiceStatus.Running)
            Debug.LogWarning("[Location] GPS fallback unavailable: " + Input.location.status);
    }

    IEnumerator CheckCoverage(double latitude, double longitude)
    {
        // Not without a session.
        //
        // CheckVpsAvailabilityAsync calls into ARCore's native library on a
        // background thread. With no session there is nothing on the other side of
        // that call and the process dies - a SIGSEGV at address zero, on a thread
        // whose stack contains no Unity frames at all, which no try/catch can see
        // because a segfault is not an exception.
        //
        // This runs the moment HasValidPose turns true, which on a fresh install is
        // the moment the location permission is granted. That is why this read as
        // "answering a permission dialog closes the app" for so long: the dialog
        // and the first fix are the same instant.
        if (ARSession.state != ARSessionState.SessionInitializing &&
            ARSession.state != ARSessionState.SessionTracking)
        {
            // Ask again later. The camera tab starts AR, and coverage is only worth
            // knowing once there is something to use it for.
            _coverageChecked = false;
            yield break;
        }

        VpsAvailabilityPromise promise =
            AREarthManager.CheckVpsAvailabilityAsync(latitude, longitude);

        yield return promise;

        CoverageHere = promise.Result;

        if (CoverageHere == VpsAvailability.Unavailable)
        {
            Debug.Log("[Location] No VPS coverage here - positions will stay GPS-grade " +
                      "and markers cannot be placed accurately.");
        }
    }

    // ----------------------------------------------------------------- distances

    /// <summary>
    /// Horizontal distance from the reader to a building, or -1 when there is no
    /// position to measure from.
    /// </summary>
    public double DistanceMetresTo(BuildingData building)
    {
        if (!HasValidPose || building == null)
            return -1d;

        return GeoMath.DistanceMetres(Latitude, Longitude, building.latitude, building.longitude);
    }

    /// <summary>Buildings inside the radius, nearest first. Empty when there's no position.</summary>
    public List<BuildingData> BuildingsWithin(double radiusMetres)
    {
        var results = new List<BuildingData>();

        if (!HasValidPose)
            return results;

        var all = BuildingDatabase.AllBuildings;
        for (int i = 0; i < all.Count; i++)
        {
            if (DistanceMetresTo(all[i]) <= radiusMetres)
                results.Add(all[i]);
        }

        results.Sort((a, b) => DistanceMetresTo(a).CompareTo(DistanceMetresTo(b)));
        return results;
    }
}
