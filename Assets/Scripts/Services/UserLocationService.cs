using System;
using System.Collections;
using System.Collections.Generic;
using Google.XR.ARCoreExtensions;
using UnityEngine;
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
        if (useGpsFallback)
            StartCoroutine(StartGps());
    }

    void OnDisable()
    {
        if (Input.location.status == LocationServiceStatus.Running)
            Input.location.Stop();
    }

    void Update()
    {
        _timer += Time.unscaledDeltaTime;
        if (_timer < pollInterval)
            return;

        _timer = 0f;
        Poll();
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

    IEnumerator StartGps()
    {
        // ARCore already asks for location permission when Geospatial mode turns on,
        // so this normally inherits it rather than prompting a second time.
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
