using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One trail in the My trails list.</summary>
public class TrailRowView : MonoBehaviour
{
    public Button button;

    [Tooltip("The trail's own colour, so a row and the map it opens agree.")]
    public Image dot;

    public TextMeshProUGUI nameLabel;

    [Tooltip("What this reader may do here, and how the trail is played.")]
    public TextMeshProUGUI metaLabel;

    [Tooltip("Marks the trail currently open. Off for every other row.")]
    public GameObject currentMark;

    public void Bind(TrailService.Trail trail, bool current)
    {
        if (trail == null)
            return;

        if (nameLabel != null)
            nameLabel.text = string.IsNullOrWhiteSpace(trail.name) ? trail.id : trail.name;

        if (metaLabel != null)
            metaLabel.text = Meta(trail);

        if (dot != null)
            dot.color = MarkerPalette.ForKey(trail.id);

        if (currentMark != null)
            currentMark.SetActive(current);
    }

    /// <summary>
    /// Role and mode, not a marker count.
    ///
    /// A count is the obvious thing to put here and it cannot be had: the markers
    /// table has no select policy, so nothing but markers_near ever returns a
    /// marker, and that answers "what is near this point" rather than "how many are
    /// there". Showing a number would mean either a second RPC written to leak one,
    /// or a guess. Role and mode are true, already loaded, and more use anyway.
    /// </summary>
    static string Meta(TrailService.Trail trail)
    {
        string role = AppStrings.Get("trails.role." +
                                     (string.IsNullOrEmpty(trail.role) ? "viewer" : trail.role));

        string mode = string.IsNullOrEmpty(trail.mode)
            ? string.Empty
            : AppStrings.Get("mode." + trail.mode);

        // Get returns the key when a string is missing, which would put
        // "trails.role.curator" on screen. Loud is right for us and wrong for them.
        if (role.StartsWith("trails.role."))
            role = trail.role;

        if (mode.StartsWith("mode."))
            mode = string.Empty;

        return string.IsNullOrEmpty(mode) ? role : role + "  ·  " + mode;
    }
}
