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

    void OnEnable()
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(Open);
    }

    void Open()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenTeam();
    }
}
