using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Detects taps on world-space building markers and opens the info panel.
/// Uses the legacy Input class deliberately; Active Input Handling is set to
/// "Both", so this coexists with AR Foundation's use of the Input System.
/// </summary>
public class CubeTapManager : MonoBehaviour
{
    [Header("References")]
    public InfoPanelController infoPanel;

    [Tooltip("Taps are ignored while this panel is hidden, so tapping around on the " +
             "Search or Settings tab can't open a building. Leave empty to always listen.")]
    public GameObject cameraPanel;

    [Header("Behaviour")]
    [Tooltip("Markers further away than this (in metres) are not tappable.")]
    public float maxTapDistance = 500f;

    [Tooltip("Tapping empty space closes an open info panel.")]
    public bool closeOnTapEmptySpace = true;

    [Header("Debug")]
    [Tooltip("Logs every step of the tap -> raycast -> panel chain. Turn off for release builds.")]
    public bool verboseLogging = false;

    // Reused every tap so repeated taps don't allocate.
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    void Update()
    {
        if (infoPanel == null)
        {
            Debug.LogError("CubeTapManager: the Info Panel field is empty.", this);
            enabled = false;
            return;
        }

        if (cameraPanel != null && !cameraPanel.activeInHierarchy)
            return;

        if (!TryGetTapThisFrame(out Vector2 screenPosition, out int pointerId))
            return;

        Log($"Tap detected at: {screenPosition}");

        if (IsPointerOverUI(pointerId))
        {
            Log("Tap was over a UI element - raycast skipped.");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("CubeTapManager: no enabled camera is tagged 'MainCamera'.");
            return;
        }

        TappableObject tappable = FindNearestTappable(cam.ScreenPointToRay(screenPosition));

        if (tappable == null)
        {
            Log("No building marker under that tap.");
            if (closeOnTapEmptySpace)
                infoPanel.ClosePanel();
            return;
        }

        if (tappable.buildingData == null)
        {
            Debug.LogWarning($"{tappable.name} has a TappableObject with an empty Building Data field.");
            return;
        }

        Log($"Opening panel for: {tappable.buildingData.buildingName}");
        infoPanel.ShowPanel(tappable.buildingData);
    }

    /// <summary>
    /// Returns the closest marker along the ray. Uses RaycastNonAlloc rather than a
    /// single Physics.Raycast so that colliders which are not markers - AR planes,
    /// Streetscape Geometry, terrain - can't sit in front of a marker and swallow
    /// the tap. Avoids needing a layer mask, which would be one more thing to keep
    /// configured correctly on every prefab.
    /// </summary>
    TappableObject FindNearestTappable(Ray ray)
    {
        int count = Physics.RaycastNonAlloc(ray, _hits, maxTapDistance);

        TappableObject nearest = null;
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            TappableObject candidate = _hits[i].collider.GetComponentInParent<TappableObject>();
            if (candidate == null)
                continue;

            if (_hits[i].distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = _hits[i].distance;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Reports the start of a tap. Reads real touches on device and falls back to
    /// the mouse in the Editor, and reports the matching EventSystem pointer id.
    /// </summary>
    bool TryGetTapThisFrame(out Vector2 screenPosition, out int pointerId)
    {
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            screenPosition = touch.position;
            pointerId = touch.fingerId;
            return touch.phase == TouchPhase.Began;
        }

        screenPosition = Input.mousePosition;
        pointerId = PointerInputModule.kMouseLeftId;
        return Input.GetMouseButtonDown(0);
    }

    /// <summary>
    /// The scene's EventSystem uses InputSystemUIInputModule while taps are read
    /// through the legacy Input class, and the two disagree about pointer ids for
    /// touch. Checking both forms is what reliably stops a tap on the Close button
    /// from also raycasting into the world behind it.
    /// </summary>
    bool IsPointerOverUI(int pointerId)
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
            return false;

        return eventSystem.IsPointerOverGameObject() || eventSystem.IsPointerOverGameObject(pointerId);
    }

    void Log(string message)
    {
        if (verboseLogging)
            Debug.Log(message);
    }
}
