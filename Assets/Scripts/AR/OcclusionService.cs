using System.Collections.Generic;
using Google.XR.ARCoreExtensions;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
// TrackableId lives in this namespace. Line 92 refers to the namespace fully
// qualified while line 55 uses TrackableId bare, so the file has been
// uncompilable since it was last edited - the original project simply never
// recompiled after that edit and kept running a stale assembly.
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Stops markers showing through solid things.
///
/// Two mechanisms, because neither covers the whole range on its own:
///
/// Streetscape Geometry supplies real building meshes hundreds of metres out. Those
/// become invisible colliders, and a marker whose line of sight crosses one is
/// hidden completely. This is what fixes a marker on a far street showing through
/// the block in front of you.
///
/// The depth API handles what is close - a car, a wall, a person - but ARCore's
/// depth is only dependable to roughly eight metres, so it cannot do the job above.
/// It is left to occlude per-pixel through the AR background shader rather than
/// being sampled per marker, which would cost a CPU image read every frame for a
/// case that resolves itself as soon as you take a step.
/// </summary>
public class OcclusionService : MonoBehaviour
{
    public static OcclusionService Instance { get; private set; }

    const string PrefKey = "occlusion_enabled";

    // Unity's built-in "Ignore Raycast" layer. Occluders live here so the marker
    // tap raycast skips them for free, while this class asks for it explicitly.
    const int OccluderLayer = 2;

    [Header("References")]
    public ARStreetscapeGeometryManager streetscapeManager;
    public AROcclusionManager occlusionManager;

    [Header("Behaviour")]
    [Tooltip("Occlude against building meshes.")]
    public bool occludeAgainstBuildings = true;

    [Tooltip("Occlude against terrain too. Off by default: markers sit only 1.5 m " +
             "above the ground, so an imprecise terrain mesh can swallow them.")]
    public bool occludeAgainstTerrain;

    [Tooltip("A marker this close to whatever the ray hits counts as being on that " +
             "surface rather than behind it - otherwise a building would hide its " +
             "own marker.")]
    public float surfaceToleranceMetres = 5f;

    [Header("Debug")]
    [Tooltip("Draw the occluder meshes instead of leaving them invisible.")]
    public bool showOccluders;

    public Material debugMaterial;

    readonly Dictionary<TrackableId, GameObject> _occluders =
        new Dictionary<TrackableId, GameObject>();

    Transform _cameraTransform;

    /// <summary>Readers can turn this off if their surroundings aren't mapped well.</summary>
    public static bool Enabled
    {
        get { return PlayerPrefs.GetInt(PrefKey, 1) == 1; }
        set
        {
            PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        if (streetscapeManager != null)
            streetscapeManager.StreetscapeGeometriesChanged += HandleGeometriesChanged;

        // Per-pixel occlusion for anything close. URP's AR background applies this
        // to everything the camera draws, markers included, with no work from us.
        if (occlusionManager != null)
        {
            occlusionManager.requestedEnvironmentDepthMode =
                UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Fastest;
        }
    }

    void OnDisable()
    {
        if (streetscapeManager != null)
            streetscapeManager.StreetscapeGeometriesChanged -= HandleGeometriesChanged;

        ClearOccluders();
    }

    // ------------------------------------------------------------- geometry

    void HandleGeometriesChanged(ARStreetscapeGeometriesChangedEventArgs args)
    {
        foreach (ARStreetscapeGeometry geometry in args.Added)
            AddOrUpdate(geometry);

        foreach (ARStreetscapeGeometry geometry in args.Updated)
            AddOrUpdate(geometry);

        foreach (ARStreetscapeGeometry geometry in args.Removed)
            Remove(geometry);
    }

    void AddOrUpdate(ARStreetscapeGeometry geometry)
    {
        if (!WantsType(geometry.streetscapeGeometryType))
        {
            Remove(geometry);
            return;
        }

        if (!_occluders.TryGetValue(geometry.trackableId, out GameObject occluder) ||
            occluder == null)
        {
            occluder = new GameObject("Occluder");
            occluder.layer = OccluderLayer;
            occluder.transform.SetParent(transform, false);
            occluder.AddComponent<MeshCollider>();

            _occluders[geometry.trackableId] = occluder;
        }

        occluder.transform.SetPositionAndRotation(geometry.pose.position, geometry.pose.rotation);

        MeshCollider collider = occluder.GetComponent<MeshCollider>();
        if (collider.sharedMesh != geometry.mesh)
            collider.sharedMesh = geometry.mesh;

        UpdateDebugRenderer(occluder, geometry.mesh);
    }

    bool WantsType(StreetscapeGeometryType type)
    {
        return type == StreetscapeGeometryType.Building
            ? occludeAgainstBuildings
            : occludeAgainstTerrain;
    }

    void Remove(ARStreetscapeGeometry geometry)
    {
        if (!_occluders.TryGetValue(geometry.trackableId, out GameObject occluder))
            return;

        _occluders.Remove(geometry.trackableId);

        if (occluder != null)
            Destroy(occluder);
    }

    void ClearOccluders()
    {
        foreach (GameObject occluder in _occluders.Values)
        {
            if (occluder != null)
                Destroy(occluder);
        }

        _occluders.Clear();
    }

    void UpdateDebugRenderer(GameObject occluder, Mesh mesh)
    {
        MeshRenderer renderer = occluder.GetComponent<MeshRenderer>();

        if (!showOccluders)
        {
            if (renderer != null)
                renderer.enabled = false;

            return;
        }

        MeshFilter filter = occluder.GetComponent<MeshFilter>();
        if (filter == null)
            filter = occluder.AddComponent<MeshFilter>();

        filter.sharedMesh = mesh;

        if (renderer == null)
            renderer = occluder.AddComponent<MeshRenderer>();

        renderer.enabled = true;

        if (debugMaterial != null)
            renderer.sharedMaterial = debugMaterial;
    }

    // -------------------------------------------------------------- querying

    /// <summary>
    /// True when solid geometry sits between the camera and this point. Fails open:
    /// with no service, no camera or occlusion switched off, nothing is ever hidden.
    /// A missing mesh should never make markers disappear.
    /// </summary>
    public static bool IsOccludedAt(Vector3 worldPosition)
    {
        OcclusionService service = Instance;

        if (service == null || !Enabled)
            return false;

        return service.Check(worldPosition);
    }

    bool Check(Vector3 worldPosition)
    {
        if (_occluders.Count == 0)
            return false;

        if (_cameraTransform == null)
        {
            Camera main = Camera.main;
            if (main == null)
                return false;

            _cameraTransform = main.transform;
        }

        Vector3 origin = _cameraTransform.position;
        Vector3 offset = worldPosition - origin;
        float distance = offset.magnitude;

        // Stop short of the marker. A marker sits on the facade of the building it
        // describes, so without this every building would hide its own marker.
        float testDistance = distance - surfaceToleranceMetres;

        if (testDistance <= 0f)
            return false;

        return Physics.Raycast(origin, offset / distance, testDistance, 1 << OccluderLayer);
    }
}
