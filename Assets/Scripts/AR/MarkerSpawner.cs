using System;
using System.Collections;
using System.Collections.Generic;
using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Creates a marker in the world for every nearby building, from coordinates alone.
///
/// This replaces placing anchors by hand in the Editor. A building exists because
/// it is a row in the spreadsheet, and its marker appears when the reader walks
/// within range - which is what makes adding a building cost no rebuild.
///
/// Terrain anchors are used rather than WGS84 ones so ARCore resolves the height
/// against real ground. Nothing has to know a building's altitude above sea level,
/// which was the single most error-prone number in the old workflow.
/// </summary>
public class MarkerSpawner : MonoBehaviour
{
    [Serializable]
    public class MarkerStyle
    {
        [Tooltip("Matches the sheet's \"marker\" column, or a Category's Marker Style.")]
        public string key;
        public GameObject prefab;
    }

    [Header("References")]
    public ARAnchorManager anchorManager;
    public UserLocationService locationService;

    [Header("Markers")]
    [Tooltip("Used when a building names no style and its category names none either.")]
    public GameObject defaultPrefab;

    [Tooltip("Named styles a building or category can ask for.")]
    public List<MarkerStyle> styles = new List<MarkerStyle>();

    [Header("Placement")]
    [Tooltip("Metres above the ground at the building's coordinates.")]
    public double altitudeAboveTerrain = 1.5;

    [Tooltip("Buildings within this range get a marker.")]
    public double spawnRadiusMetres = 500d;

    [Tooltip("Released once the reader is this far away. Wider than the spawn radius " +
             "so a marker doesn't flicker while standing near the boundary.")]
    public double releaseRadiusMetres = 650d;

    [Tooltip("ARCore only tracks a limited number of anchors well. Nearest win.")]
    public int maxActiveMarkers = 20;

    [Header("Debug")]
    public bool verboseLogging;

    class Placed
    {
        public ARGeospatialAnchor anchor;
        public GameObject marker;
    }

    readonly Dictionary<string, Placed> _placed = new Dictionary<string, Placed>();
    readonly HashSet<string> _resolving = new HashSet<string>();

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        if (locationService != null)
            locationService.OnPoseUpdated += Sync;

        BuildingDatabase.OnBuildingsChanged += HandleBuildingsChanged;

        LightFromAmbientColour();

        if (verboseLogging)
            Debug.Log("[Markers] Ambient light " + RenderSettings.ambientMode + " " +
                      RenderSettings.ambientProbe[0, 0].ToString("F2"));
    }

    /// <summary>
    /// A phone keeps the scene's ambient colour but not the light made from it:
    /// without generated lighting the ambient probe loads as zeros (seen in
    /// 0.4.4's log), and every side of a marker the sun misses renders black.
    /// So the probe is built from the colour, as AR Foundation's light
    /// estimation samples do. MarkerPreview calls this too, so it shows what a
    /// phone shows.
    /// </summary>
    public static void LightFromAmbientColour()
    {
        var ambient = new UnityEngine.Rendering.SphericalHarmonicsL2();
        ambient.AddAmbientLight(RenderSettings.ambientLight);
        RenderSettings.ambientProbe = ambient;
    }

    void OnDisable()
    {
        if (locationService != null)
            locationService.OnPoseUpdated -= Sync;

        BuildingDatabase.OnBuildingsChanged -= HandleBuildingsChanged;
    }

    /// <summary>
    /// The marker standing in the world for this stop, or null when there is
    /// none - out of range, still resolving, or Earth tracking not up. Capture
    /// mode moves it inside its anchor, whose axes are east, up and south.
    /// </summary>
    public Transform MarkerFor(string buildingId)
    {
        return buildingId != null && _placed.TryGetValue(buildingId, out Placed placed) &&
               placed.marker != null
            ? placed.marker.transform
            : null;
    }

    /// <summary>
    /// Takes one marker down so the next sync places it again, at whatever
    /// coordinates its BuildingData now holds. Used after a stop is moved.
    /// </summary>
    public void Replace(string buildingId)
    {
        Release(buildingId);
    }

    void HandleBuildingsChanged()
    {
        // A building may have been unpublished or moved, so drop everything and let
        // the next sync rebuild from the new list.
        ClearAll();
    }

    /// <summary>
    /// Called on the location service's own once-a-second cadence. Adds markers that
    /// have come into range and releases ones left behind.
    /// </summary>
    void Sync()
    {
        // HasPrecisePose, not HasValidPose: a marker placed from a GPS fix would sit
        // on the wrong building, and a wrong marker is worse than no marker.
        if (locationService == null || !locationService.HasPrecisePose)
            return;

        if (anchorManager == null)
            return;

        ReleaseDistant();
        SpawnNearby();
    }

    void SpawnNearby()
    {
        var candidates = new List<BuildingData>();

        foreach (BuildingData building in BuildingDatabase.AllBuildings)
        {
            if (building == null || string.IsNullOrWhiteSpace(building.buildingId))
                continue;

            if (_placed.ContainsKey(building.buildingId) ||
                _resolving.Contains(building.buildingId))
            {
                continue;
            }

            if (locationService.DistanceMetresTo(building) <= spawnRadiusMetres)
                candidates.Add(building);
        }

        candidates.Sort((a, b) =>
            locationService.DistanceMetresTo(a).CompareTo(locationService.DistanceMetresTo(b)));

        foreach (BuildingData building in candidates)
        {
            if (_placed.Count + _resolving.Count >= maxActiveMarkers)
                break;

            _resolving.Add(building.buildingId);
            StartCoroutine(Resolve(building));
        }
    }

    IEnumerator Resolve(BuildingData building)
    {
        ResolveAnchorOnTerrainPromise promise = anchorManager.ResolveAnchorOnTerrainAsync(
            building.latitude, building.longitude, altitudeAboveTerrain, Quaternion.identity);

        yield return promise;

        _resolving.Remove(building.buildingId);

        ResolveAnchorOnTerrainResult result = promise.Result;

        if (result.TerrainAnchorState != TerrainAnchorState.Success || result.Anchor == null)
        {
            // Expected rather than exceptional: resolution fails indoors, in poor
            // VPS coverage, and before Earth tracking settles. The next sync retries.
            Log($"Couldn't place \"{building.DisplayName}\": {result.TerrainAnchorState}");
            yield break;
        }

        // The reader may have walked out of range while this was resolving.
        if (locationService.DistanceMetresTo(building) > releaseRadiusMetres)
        {
            Destroy(result.Anchor.gameObject);
            yield break;
        }

        GameObject marker = Instantiate(ChoosePrefab(building), result.Anchor.transform);
        marker.transform.localPosition = Vector3.zero;
        marker.transform.localRotation = Quaternion.identity;

        // This is what makes the existing tap handling work unchanged - it looks for
        // a TappableObject on whatever the ray hits and doesn't care who made it.
        TappableObject tappable = marker.GetComponent<TappableObject>();
        if (tappable == null)
            tappable = marker.AddComponent<TappableObject>();

        tappable.buildingData = building;

        MarkerView view = marker.GetComponent<MarkerView>();
        if (view != null)
        {
            view.SetColour(ChooseColour(building));
            view.SetSign(building.SignText);
        }

        _placed[building.buildingId] = new Placed
        {
            anchor = result.Anchor,
            marker = marker
        };

        Log($"Placed \"{building.DisplayName}\".");
    }

    void ReleaseDistant()
    {
        var stale = new List<string>();

        foreach (var pair in _placed)
        {
            BuildingData building = BuildingDatabase.GetById(pair.Key);

            if (building == null ||
                locationService.DistanceMetresTo(building) > releaseRadiusMetres)
            {
                stale.Add(pair.Key);
            }
        }

        foreach (string id in stale)
            Release(id);
    }

    void Release(string buildingId)
    {
        if (!_placed.TryGetValue(buildingId, out Placed placed))
            return;

        _placed.Remove(buildingId);

        // Destroying the anchor takes the marker parented under it with it.
        if (placed.anchor != null)
            Destroy(placed.anchor.gameObject);
        else if (placed.marker != null)
            Destroy(placed.marker);
    }

    void ClearAll()
    {
        foreach (string id in new List<string>(_placed.Keys))
            Release(id);

        _resolving.Clear();
    }

    // ------------------------------------------------------------- appearance

    GameObject ChoosePrefab(BuildingData building)
    {
        GameObject fromBuilding = FindStyle(building.markerStyle);
        if (fromBuilding != null)
            return fromBuilding;

        if (building.category != null)
        {
            GameObject fromCategory = FindStyle(building.category.markerStyle);
            if (fromCategory != null)
                return fromCategory;
        }

        return defaultPrefab;
    }

    GameObject FindStyle(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        foreach (MarkerStyle style in styles)
        {
            if (style != null && style.prefab != null &&
                string.Equals(style.key, key.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return style.prefab;
            }
        }

        Log($"No marker style named \"{key}\" - falling back.");
        return null;
    }

    /// <summary>
    /// Delegated so the cube in the world, the pin on the map and the dot on the
    /// sheet can never disagree about a marker's colour.
    /// </summary>
    static Color ChooseColour(BuildingData building)
    {
        return MarkerPalette.Resolve(building);
    }

    void Log(string message)
    {
        if (verboseLogging)
            Debug.Log("[Markers] " + message);
    }
}
