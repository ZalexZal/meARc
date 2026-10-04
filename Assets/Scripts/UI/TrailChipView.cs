using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The glass pill at the top of the map: which trail is open, how it is going,
/// and the way to change it.
///
///   Кой знае повече?
///   Състезание · 2 от 5 · 20 точки                         ⌄
///
/// Daylight v2 put everything a walker asks about the trail on this one line
/// and spelled it out: the kind of trail by name, stops found out of the
/// trail's own count, and points written as a word rather than "т.". It
/// replaced a "TRAIL" eyebrow, a mode chip and a warning triangle, three marks
/// that each needed learning.
/// </summary>
public class TrailChipView : MonoBehaviour
{
    public Button button;
    public TextMeshProUGUI nameLabel;

    [Tooltip("Kind of trail · found of total · points.")]
    public TextMeshProUGUI subLabel;

    [Tooltip("Kept so older scenes load. The kind of trail is on the sub line now.")]
    public GameObject modeChip;
    public TextMeshProUGUI modeLabel;

    [Tooltip("The chevron that says this is a control rather than a heading.")]
    public GameObject chevron;

    [Tooltip("Kept so older scenes load. Reporting a trail lives in My trails now.")]
    public Button reportButton;
    public ReportSheetView reportSheet;

    void Awake()
    {
        if (button != null)
            button.onClick.AddListener(Open);

        if (reportButton != null)
            reportButton.onClick.AddListener(Report);
    }

    void Report()
    {
        TrailService trails = TrailService.Instance;

        if (reportSheet != null && trails != null && !string.IsNullOrEmpty(trails.SelectedId))
            reportSheet.OpenTrail(trails.SelectedId);
    }

    void OnEnable()
    {
        TrailService.OnTrailsChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        DemoTrail.OnChanged += Refresh;
        DiscoveryTracker.OnDiscoveryChanged += Refresh;
        MarkerTaskService.OnProgressChanged += Scored;
        TeamService.OnTeamChanged += Refresh;
        BuildingDatabase.OnBuildingsChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        TrailService.OnTrailsChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        DemoTrail.OnChanged -= Refresh;
        DiscoveryTracker.OnDiscoveryChanged -= Refresh;
        MarkerTaskService.OnProgressChanged -= Scored;
        TeamService.OnTeamChanged -= Refresh;
        BuildingDatabase.OnBuildingsChanged -= Refresh;
    }

    /// <summary>
    /// An answer or a hint moved the team's points, and those are the server's
    /// to say, so they are asked for again. The redraw follows OnTeamChanged.
    /// </summary>
    void Scored()
    {
        Refresh();

        TrailService trails = TrailService.Instance;
        TeamService teams = TeamService.Instance;

        if (trails != null && trails.SelectedIsGame && teams != null && !teams.Busy &&
            !string.IsNullOrEmpty(trails.SelectedId))
        {
            teams.Load(trails.SelectedId);
        }
    }

    void Open()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenTrails();
    }

    public void Refresh()
    {
        TrailService service = TrailService.Instance;
        TrailService.Trail trail = service != null ? service.Selected : null;

        if (DemoTrail.Active)
            trail = new TrailService.Trail { name = AppStrings.Get("demo.name"), mode = "guide" };

        bool hasName = trail != null && !string.IsNullOrWhiteSpace(trail.name);

        if (nameLabel != null)
            nameLabel.text = hasName ? trail.name : AppStrings.Get("trails.choose");

        if (modeChip != null && modeChip.activeSelf)
            modeChip.SetActive(false);

        if (reportButton != null && reportButton.gameObject.activeSelf)
            reportButton.gameObject.SetActive(false);

        if (subLabel != null)
            subLabel.text = hasName ? Line(trail, service) : AppStrings.Get("chip.none");

        if (chevron != null)
            chevron.SetActive(service != null);

        if (button != null)
            button.interactable = service != null;
    }

    /// <summary>"Състезание · 2 от 5 · 20 точки", with each part only when it is true.</summary>
    static string Line(TrailService.Trail trail, TrailService service)
    {
        var parts = new System.Collections.Generic.List<string>();

        string mode = string.IsNullOrEmpty(trail.mode) ? "guide" : trail.mode;
        string named = AppStrings.Get("mode." + mode);
        parts.Add(named.StartsWith("mode.") ? mode : named);

        var all = BuildingDatabase.AllBuildings;
        int total = all.Count;
        if (total > 0)
        {
            int done = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (NextStopCard.Done(all[i]))
                    done++;
            }

            parts.Add(string.Format(AppStrings.Get("chip.progress"), done, total));
        }

        TeamService teams = TeamService.Instance;
        // Not in the demo: the team and its points belong to the real trail
        // underneath it, and a guided walk scores nothing.
        if (!DemoTrail.Active && service != null && service.SelectedIsGame && teams != null &&
            teams.Team != null && teams.Team.on_team)
        {
            parts.Add(string.Format(AppStrings.Get("chip.points"), teams.Team.points));
        }

        return string.Join(" · ", parts);
    }
}
