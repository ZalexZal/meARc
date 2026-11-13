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

    [Tooltip("The pull-down the chip opens; without one it opens the trail screen.")]
    public TrailMenuView menu;

    [Tooltip("Kept so older scenes load. Reporting a trail lives in My trails now.")]
    public Button reportButton;
    public ReportSheetView reportSheet;

    void Awake()
    {
        if (button != null)
            button.onClick.AddListener(Open);

        // Reporting is switched off for the competition build: no listener, and
        // the button stays hidden (Refresh keeps it so).
        if (reportButton != null)
            reportButton.gameObject.SetActive(false);
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
        // The chevron says "menu": a pull-down of the trails joined, with the
        // full trail screen one row further (design feedback, 6 Nov 2026).
        if (menu != null)
        {
            menu.Toggle();
            return;
        }

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
            nameLabel.text = hasName ? trail.name : AppStrings.Get("trails.title");

        if (modeChip != null && modeChip.activeSelf)
            modeChip.SetActive(false);

        if (reportButton != null && reportButton.gameObject.activeSelf)
            reportButton.gameObject.SetActive(false);

        // No trail: one word ("Trails") and no second line. The card under it
        // already says what to do; the chip said it twice more (scan, 6 Nov).
        if (subLabel != null)
        {
            subLabel.text = hasName ? Line(trail, service) : string.Empty;
            if (subLabel.gameObject.activeSelf != hasName)
                subLabel.gameObject.SetActive(hasName);
        }

        if (chevron != null)
            chevron.SetActive(service != null);

        if (button != null)
            button.interactable = service != null;
    }

    /// <summary>"Състезание · 2 от 5 · 20 точки", with each part only when it is true.</summary>
    static string Line(TrailService.Trail trail, TrailService service)
    {
        var parts = new System.Collections.Generic.List<string>();

        // Only the order here: the full mode line ("Teams & standings · In
        // order") is on the trail's row, and the points further along already
        // say there are teams. One line on the map, never an ellipsis.
        parts.Add(AppStrings.Get(TrailService.IsOrdered(trail.mode) ? "chip.inOrder" : "chip.anyOrder"));

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
