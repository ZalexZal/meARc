using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opens the team card from the map.
///
/// Resolved at runtime rather than wired at build time, for the same reason
/// TrailChipView is: the doors are built after the map, so there is no DoorFlow
/// to point at when this button is made.
/// </summary>
public class OpenTeamButton : MonoBehaviour
{
    public Button button;

    [Tooltip("Hidden on a walk. Shown, it offers a screen the server will " +
             "refuse - a guide has no teams at all.")]
    public GameObject holder;

    void OnEnable()
    {
        TrailService.OnTrailsChanged += Apply;
        Apply();

        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Open);
    }

    void OnDisable()
    {
        TrailService.OnTrailsChanged -= Apply;
    }

    /// <summary>
    /// A walk has no teams. Offering the button anyway is offering a door into a
    /// room that does not exist: create_team refuses on a guide, so the only
    /// thing behind it would be an error.
    /// </summary>
    void Apply()
    {
        GameObject target = holder != null ? holder : gameObject;
        TrailService trails = TrailService.Instance;

        target.SetActive(trails != null && trails.SelectedIsGame);
    }

    void Open()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenTeam();
    }
}
