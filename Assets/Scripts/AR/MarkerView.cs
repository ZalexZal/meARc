using TMPro;
using UnityEngine;

/// <summary>
/// One marker in the world. Faces the reader, and stays visible at range without
/// pretending to be closer than it is.
/// </summary>
public class MarkerView : MonoBehaviour
{
    [Header("Appearance")]
    [Tooltip("Renderers tinted by the building's or category's marker colour.")]
    public Renderer[] tintedRenderers;

    [Tooltip("The words on a sign-shaped marker. Empty on every other shape.")]
    public TextMeshPro signLabel;

    [Header("Scale")]
    [Tooltip("Real-world size in metres at close range.")]
    public float baseScale = 1.5f;

    [Tooltip("Beyond this distance the marker stops shrinking, so a building across " +
             "the square is still visible and tappable.")]
    public float clampDistance = 60f;

    [Tooltip("Turn the marker to face the camera. Right for a flat or pin-shaped " +
             "marker; turn it off for something with a meaningful orientation.")]
    public bool billboard = true;

    [Header("Occlusion")]
    [Tooltip("Seconds between line-of-sight checks. Every frame would be wasteful " +
             "for something that only changes as you walk or turn.")]
    public float occlusionCheckInterval = 0.15f;

    MaterialPropertyBlock _block;
    Transform _camera;

    Renderer[] _renderers;
    Collider[] _colliders;
    float _nextOcclusionCheck;
    bool _visible = true;

    void Awake()
    {
        _block = new MaterialPropertyBlock();

        _renderers = GetComponentsInChildren<Renderer>(true);
        _colliders = GetComponentsInChildren<Collider>(true);

        // Spread the checks across frames so twenty markers never all raycast on
        // the same one.
        _nextOcclusionCheck = Random.value * occlusionCheckInterval;
    }

    void LateUpdate()
    {
        if (_camera == null)
        {
            Camera main = Camera.main;
            if (main == null)
                return;

            _camera = main.transform;
        }

        float distance = Vector3.Distance(_camera.position, transform.position);

        // Below the clamp the marker behaves like a real object and shrinks with
        // distance. Past it, the on-screen size holds steady - otherwise anything
        // across a square becomes a couple of pixels and effectively disappears.
        float scale = distance <= clampDistance
            ? baseScale
            : baseScale * (distance / clampDistance);

        transform.localScale = Vector3.one * scale;

        if (Time.unscaledTime >= _nextOcclusionCheck)
        {
            _nextOcclusionCheck = Time.unscaledTime + occlusionCheckInterval;
            SetVisible(!OcclusionService.IsOccludedAt(transform.position));
        }

        if (billboard)
        {
            Vector3 toCamera = _camera.position - transform.position;
            toCamera.y = 0f; // stay upright; only turn about the vertical axis

            if (toCamera.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-toCamera.normalized, Vector3.up);
        }
    }

    /// <summary>
    /// Hides or shows the whole marker. The collider goes with the renderer, so a
    /// marker you cannot see is also a marker you cannot accidentally tap through
    /// the wall hiding it.
    /// </summary>
    void SetVisible(bool visible)
    {
        if (_visible == visible)
            return;

        _visible = visible;

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].enabled = visible;
        }

        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] != null)
                _colliders[i].enabled = visible;
        }
    }

    /// <summary>What a sign says. Does nothing on a shape with nowhere to write.</summary>
    public void SetSign(string words)
    {
        if (signLabel == null)
            return;

        // "Name · date" breaks after the dot, not before it.
        signLabel.text = (words ?? string.Empty).Replace(" · ", " · ");
        signLabel.ForceMeshUpdate();
    }

    /// <summary>
    /// Tints without instancing a material per marker, so a hundred markers still
    /// share one material and one draw call's worth of state.
    /// </summary>
    public void SetColour(Color colour)
    {
        if (tintedRenderers == null)
            return;

        if (_block == null)
            _block = new MaterialPropertyBlock();

        for (int i = 0; i < tintedRenderers.Length; i++)
        {
            if (tintedRenderers[i] == null)
                continue;

            tintedRenderers[i].GetPropertyBlock(_block);

            // URP's Lit shader reads _BaseColor; the older built-in name is kept
            // as well so a marker made from a default material still tints.
            _block.SetColor("_BaseColor", colour);
            _block.SetColor("_Color", colour);

            tintedRenderers[i].SetPropertyBlock(_block);
        }
    }
}
