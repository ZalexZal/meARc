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

    [Header("Leaving")]
    public Button leaveButton;
    public TextMeshProUGUI leaveLabel;

    [Tooltip("Seconds an armed Leave waits before going quiet again.")]
    public float armedSeconds = 4f;

    /// <summary>Set by the list. Given the trail this row is showing.</summary>
    public System.Action<TrailService.Trail> onLeave;

    TrailService.Trail _trail;
    bool _armed;
    float _armedAt;

    void Awake()
    {
        if (leaveButton != null)
            leaveButton.onClick.AddListener(Tap);
    }

    void OnDisable()
    {
        Disarm();
    }

    void Update()
    {
        // An armed row left alone goes quiet rather than waiting all day for a tap
        // the reader has stopped intending.
        if (_armed && Time.unscaledTime - _armedAt > armedSeconds)
            Disarm();
    }

    /// <summary>
    /// First tap arms, second leaves.
    ///
    /// This sits a centimetre from the control that opens the trail, and undoing
    /// it means finding whoever has the code. A dialog would be the other way to
    /// do it; two taps on the same control is fewer moving parts and says the same
    /// thing, as long as the label changes - which is the part that makes it a
    /// question rather than a delay.
    /// </summary>
    void Tap()
    {
        if (!_armed)
        {
            _armed = true;
            _armedAt = Time.unscaledTime;
            DrawLeave();
            return;
        }

        _armed = false;
        DrawLeave();

        if (onLeave != null && _trail != null)
            onLeave(_trail);
    }

    public void Disarm()
    {
        if (!_armed)
            return;

        _armed = false;
        DrawLeave();
    }

    void DrawLeave()
    {
        if (leaveLabel == null)
            return;

        leaveLabel.text = AppStrings.Get(_armed ? "trails.leaveConfirm" : "trails.leave");
        leaveLabel.color = ThemeManager.Get(_armed ? ThemeRole.StatusPoor : ThemeRole.TextMuted);
    }

    public void Bind(TrailService.Trail trail, bool current)
    {
        if (trail == null)
            return;

        _trail = trail;

        // A row reused for a different trail must not still be holding the last
        // one's armed Leave.
        _armed = false;
        DrawLeave();

        if (nameLabel != null)
            nameLabel.text = string.IsNullOrWhiteSpace(trail.name) ? trail.id : trail.name;

        if (metaLabel != null)
            metaLabel.text = Meta(trail);

        if (dot != null)
            dot.color = MarkerPalette.ForKey(trail.id);

        if (currentMark != null)
            currentMark.SetActive(current);

        if (leaveButton != null)
            leaveButton.gameObject.SetActive(true);
    }

    /// <summary>
    /// A public trail the reader has not joined. The whole row joins it, so
    /// there is nothing to leave; the line under the name is the preview -
    /// how it is played, how long it is and how far away it starts.
    /// </summary>
    public void BindListed(TrailService.Listed listed)
    {
        if (listed == null)
            return;

        Bind(new TrailService.Trail { id = listed.id, name = listed.name, mode = listed.mode }, false);

        if (leaveButton != null)
            leaveButton.gameObject.SetActive(false);

        if (metaLabel != null)
        {
            metaLabel.text = string.Format(AppStrings.Get("near.meta"),
                Meta(new TrailService.Trail { mode = listed.mode }),
                listed.stop_count,
                AppStrings.FormatDistance(listed.distance_m));
        }
    }

    /// <summary>
    /// The mode, and not a marker count.
    ///
    /// A count is the obvious thing to put here and it cannot be had: the markers
    /// table has no select policy, so nothing but markers_near ever returns a
    /// marker, and that answers "what is near this point" rather than "how many are
    /// there". Showing a number would mean either a second RPC written to leak one,
    /// or a guess.
    ///
    /// The role used to sit here beside the mode, and it was the wrong half of the
    /// pair. Viewer and Editor are permission levels on the website - they decide
    /// who may change a stop - and this row is read by somebody deciding which
    /// trail to walk, to whom "Viewer" says nothing at all. The mode is the half
    /// that changes the walk, so the mode is the half that stays.
    /// </summary>
    static string Meta(TrailService.Trail trail)
    {
        string mode = string.IsNullOrEmpty(trail.mode)
            ? string.Empty
            : AppStrings.Get("mode." + trail.mode);

        // Get returns the key when a string is missing, which would put "mode.ctf"
        // on screen. Loud is right for us and wrong for them.
        return mode.StartsWith("mode.") ? string.Empty : mode;
    }
}
