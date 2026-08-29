using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Tells the reader when they have walked within reach of a marker they have not
/// seen yet.
///
/// This is the in-app half of "notify me about markers nearby". It fires while the
/// app is open, which is the case this is actually for: someone walking a trail
/// with the map in their hand does not want to be looking at the map the whole
/// time, and a marker twenty metres behind them is one they will otherwise miss.
///
/// The other half - a notification while the app is closed - is a different piece
/// of work: it needs Unity's mobile notifications package, Android background
/// location permission, and a foreground service to keep a fix. That is not
/// pretended at here, and the setting says what this actually does.
/// </summary>
public class ProximityAlerts : MonoBehaviour
{
    public const string PrefKey = "notify_nearby";

    [Header("References")]
    public UserLocationService locationService;

    [Header("Behaviour")]
    [Tooltip("Close enough that turning round is worth it.")]
    public double alertRadiusMetres = 60d;

    [Tooltip("Nothing is announced twice until the reader has left and come back " +
             "past this, so standing on the boundary does not chime repeatedly.")]
    public double rearmRadiusMetres = 110d;

    [Tooltip("Even with several markers coming into range at once, one at a time.")]
    public float secondsBetweenAlerts = 12f;

    /// <summary>Raised with the marker that has just come into range.</summary>
    public static event Action<BuildingData> OnAlert;

    public static bool Enabled
    {
        get { return PlayerPrefs.GetInt(PrefKey, 1) == 1; }
        set
        {
            PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    // Markers currently announced, so each one fires once per approach rather than
    // once per second while the reader stands beside it.
    readonly HashSet<string> _announced = new HashSet<string>();

    float _lastAlertTime = -999f;

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (locationService != null)
            locationService.OnPoseUpdated += Check;

        BuildingDatabase.OnBuildingsChanged += Forget;
    }

    void OnDisable()
    {
        if (locationService != null)
            locationService.OnPoseUpdated -= Check;

        BuildingDatabase.OnBuildingsChanged -= Forget;
    }

    /// <summary>
    /// The list was replaced, so ids in the announced set may no longer mean the
    /// same marker. Cheaper and safer to start again than to reconcile.
    /// </summary>
    void Forget()
    {
        _announced.Clear();
    }

    void Check()
    {
        if (!Enabled || locationService == null || !locationService.HasValidPose)
            return;

        BuildingData best = null;
        double bestDistance = double.MaxValue;

        IReadOnlyList<BuildingData> all = BuildingDatabase.AllBuildings;

        for (int i = 0; i < all.Count; i++)
        {
            BuildingData marker = all[i];
            if (marker == null || string.IsNullOrWhiteSpace(marker.buildingId))
                continue;

            double distance = locationService.DistanceMetresTo(marker);
            if (distance < 0d)
                continue;

            bool announced = _announced.Contains(marker.buildingId);

            // Walked away again, so this one is worth mentioning next time.
            if (announced && distance > rearmRadiusMetres)
            {
                _announced.Remove(marker.buildingId);
                continue;
            }

            if (announced || distance > alertRadiusMetres)
                continue;

            // Already read about, so being told it is nearby is telling the reader
            // something they know.
            if (DiscoveryTracker.IsDiscovered(marker))
                continue;

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = marker;
            }
        }

        if (best == null)
            return;

        if (Time.unscaledTime - _lastAlertTime < secondsBetweenAlerts)
            return;

        _lastAlertTime = Time.unscaledTime;
        _announced.Add(best.buildingId);

        if (OnAlert != null)
            OnAlert(best);
    }
}
