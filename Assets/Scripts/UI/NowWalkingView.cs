using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Now walking" on Profile: the trail that is open and, on a scored one, the
/// team - each a row that opens its own sheet.
/// </summary>
public class NowWalkingView : MonoBehaviour
{
    [Tooltip("The group label and the group, off when no trail is open.")]
    public GameObject[] holders = new GameObject[0];

    public Button trailButton;
    public TextMeshProUGUI trailName;
    public TextMeshProUGUI trailSub;

    public Button teamButton;
    public TextMeshProUGUI teamName;
    public TextMeshProUGUI teamSub;

    void Awake()
    {
        if (trailButton != null)
            trailButton.onClick.AddListener(OpenTrails);

        if (teamButton != null)
            teamButton.onClick.AddListener(OpenTeam);
    }

    static void OpenTrails()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenTrails();
    }

    static void OpenTeam()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenTeam();
    }

    void OnEnable()
    {
        TrailService.OnTrailsChanged += Refresh;
        TeamService.OnTeamChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        MarkerTaskService.OnProgressChanged += Refresh;
        DiscoveryTracker.OnDiscoveryChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        TrailService.OnTrailsChanged -= Refresh;
        TeamService.OnTeamChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        MarkerTaskService.OnProgressChanged -= Refresh;
        DiscoveryTracker.OnDiscoveryChanged -= Refresh;
    }

    public void Refresh()
    {
        TrailService trails = TrailService.Instance;
        TrailService.Trail trail = trails != null ? trails.Selected : null;
        bool any = trail != null && !string.IsNullOrWhiteSpace(trail.name) && !DemoTrail.Active;

        for (int i = 0; i < holders.Length; i++)
        {
            if (holders[i] != null && holders[i].activeSelf != any)
                holders[i].SetActive(any);
        }

        if (!any)
            return;

        var all = BuildingDatabase.AllBuildings;
        int done = 0;
        for (int i = 0; i < all.Count; i++)
        {
            if (NextStopCard.Done(all[i]))
                done++;
        }

        string progress = string.Format(AppStrings.Get("chip.progress"), done, all.Count);
        TeamService teams = TeamService.Instance;
        TeamService.Detail team = teams != null ? teams.Team : null;
        bool scored = trails.SelectedIsGame;
        bool onTeam = scored && team != null && team.on_team && !string.IsNullOrEmpty(team.name);

        if (trailName != null)
            trailName.text = trail.name;

        if (trailSub != null)
        {
            trailSub.text = onTeam
                ? progress + " · " + string.Format(AppStrings.Get("chip.points"), team.points)
                : progress;
        }

        if (teamButton != null && teamButton.gameObject.activeSelf != scored)
            teamButton.gameObject.SetActive(scored);

        if (!scored)
            return;

        if (teamName != null)
        {
            teamName.text = onTeam ? string.Format(AppStrings.Get("now.team"), team.name)
                                   : AppStrings.Get("chip.noTeam");
        }

        if (teamSub == null)
            return;

        int rank = 0, of = 0;
        if (onTeam && teams.Board != null)
        {
            of = teams.Board.Count;
            for (int i = 0; i < teams.Board.Count; i++)
            {
                if (teams.Board[i].is_mine)
                {
                    rank = teams.Board[i].rank;
                    break;
                }
            }
        }

        teamSub.gameObject.SetActive(rank > 0);
        if (rank > 0)
            teamSub.text = string.Format(AppStrings.Get("now.place"), TeamChipView.Ordinal(rank), of);
    }
}
