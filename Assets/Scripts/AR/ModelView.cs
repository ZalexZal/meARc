using System;
using System.IO;
using System.Threading.Tasks;
using GLTFast;
using UnityEngine;

/// <summary>
/// A creator's own model standing at a stop, at its real size.
///
/// Unlike a MarkerView it neither faces the reader nor holds its size at
/// range: a statue has a front, and it is as big as the creator said it is.
/// It shares MarkerView's line-of-sight check, so a building still hides it.
///
/// The transform this sits on is the stop's anchor point on the ground. Under
/// it the model is turned to its heading, scaled to its height, and moved so
/// the middle of its base is exactly on that point.
/// </summary>
public class ModelView : MonoBehaviour
{
    public float occlusionCheckInterval = 0.15f;

    GltfImport _import;
    Renderer[] _renderers = new Renderer[0];
    Collider _collider;
    Vector3 _middle;
    float _nextOcclusionCheck;
    bool _visible = true;

    /// <summary>
    /// Loads the file and stands it here. False when it would not load; the
    /// caller puts a pin up instead.
    /// </summary>
    public async Task<bool> Show(string file, float heightMetres, int headingDegrees)
    {
        try
        {
            byte[] data = await Task.Run(() => File.ReadAllBytes(file));
            if (this == null)
                return false;

            // On the phone the work is spread over frames so the camera never
            // stutters; the editor preview has no frames to spread it over.
            _import = new GltfImport(deferAgent: Application.isPlaying ? null : new UninterruptedDeferAgent());
            bool ok = await _import.Load(data, new Uri(file), new ImportSettings
            {
                AnimationMethod = AnimationMethod.Legacy,
            });
            if (this == null || !ok)
                return false;

            // The model inside an empty that is moved to put its base on the point.
            Transform inner = new GameObject("Model").transform;
            inner.SetParent(transform, false);

            // Meshes and their animation only: a camera or a light inside a
            // downloaded file would take over the AR view or relight the street.
            var instantiator = new GameObjectInstantiator(_import, inner, null, new InstantiationSettings
            {
                Mask = ComponentType.Mesh | ComponentType.Animation,
            });
            ok = await _import.InstantiateMainSceneAsync(instantiator);
            if (this == null || !ok)
                return false;

            Fit(inner, heightMetres, headingDegrees);
            Animate(instantiator.SceneInstance.LegacyAnimation);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Models] " + file + " would not load: " + exception.Message);
            return false;
        }
    }

    void Fit(Transform inner, float heightMetres, int headingDegrees)
    {
        transform.localScale = Vector3.one;
        transform.localRotation = Quaternion.identity;

        _renderers = GetComponentsInChildren<Renderer>(true);

        // Measured in this transform's own space, at its unscaled, unturned size.
        Bounds bounds = new Bounds();
        bool first = true;
        foreach (Renderer r in _renderers)
        {
            // The mesh's own bounds, in its own node's space - a skinned
            // mesh's renderer bounds are kept relative to its root bone.
            MeshFilter filter = r.GetComponent<MeshFilter>();
            Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                      : filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                continue;

            Bounds local = mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 here = transform.InverseTransformPoint(r.transform.TransformPoint(corner));

                if (first)
                {
                    bounds = new Bounds(here, Vector3.zero);
                    first = false;
                }
                else
                {
                    bounds.Encapsulate(here);
                }
            }
        }

        float height = Mathf.Max(bounds.size.y, 0.001f);
        inner.localPosition = -new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        // The anchor's axes are east, up and south, and a glTF model's front
        // looks along its +Z - south. Turned so its front faces the heading.
        transform.localRotation = Quaternion.Euler(0f, 180f - headingDegrees, 0f);
        transform.localScale = Vector3.one * (heightMetres / height);

        // One box around it all, for the tap.
        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, height * 0.5f, 0f);
        box.size = new Vector3(Mathf.Max(bounds.size.x, 0.2f), height, Mathf.Max(bounds.size.z, 0.2f));
        _collider = box;
        _middle = box.center;
    }

    static void Animate(Animation animation)
    {
        if (animation == null || animation.clip == null)
            return;

        // Off screen it stops, as the pins' bob does.
        animation.cullingType = AnimationCullingType.BasedOnRenderers;
        animation.wrapMode = WrapMode.Loop;
        foreach (AnimationState state in animation)
            state.wrapMode = WrapMode.Loop;

        animation.Play();
    }

    void LateUpdate()
    {
        if (_collider == null || Time.unscaledTime < _nextOcclusionCheck)
            return;

        _nextOcclusionCheck = Time.unscaledTime + occlusionCheckInterval;
        bool visible = !OcclusionService.IsOccludedAt(transform.TransformPoint(_middle));
        if (visible == _visible)
            return;

        _visible = visible;
        foreach (Renderer r in _renderers)
            if (r != null) r.enabled = visible;
        _collider.enabled = visible;
    }

    void OnDestroy()
    {
        // The meshes, textures and materials glTFast made are freed with it.
        _import?.Dispose();
    }
}
