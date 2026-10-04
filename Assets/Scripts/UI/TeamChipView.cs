using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The small glass chip under the trail's name on a scored trail: your team and
/// where it stands, one tap from the team card.
///
///   👥 Северняците · 3-то място  ›
///
/// It replaced the Team button that sat at the bottom of the next-stop strip,
/// where a walker looking for their standing never looked. On a walk it is not
/// there at all - a guided trail has no teams, and a chip offering one would be
/// offering a screen the server refuses.
/// </summary>
public class TeamChipView : MonoBehaviour
{
    public Button button;
    public TextMeshProUGUI label;

    [Tooltip("Switched off on a trail without teams. Its own object, so the " +
             "component keeps listening while the chip is away.")]
    public GameObject holder;

    void Awake()
    {
        if (button != null)
            button.onClick.AddListener(Open);
    }

    void OnEnable()
    {
        TrailService.OnTrailsChanged += Refresh;
        TeamService.OnTeamChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        DemoTrail.OnChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        TrailService.OnTrailsChanged -= Refresh;
        TeamService.OnTeamChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        DemoTrail.OnChanged -= Refresh;
    }

    void Open()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenTeam();
    }

    public void Refresh()
    {
        TrailService trails = TrailService.Instance;
        bool scored = trails != null && trails.SelectedIsGame && !DemoTrail.Active;

        GameObject target = holder != null ? holder : gameObject;
        if (target.activeSelf != scored)
            target.SetActive(scored);

        if (!scored || label == null)
            return;

        TeamService teams = TeamService.Instance;

        // The chip names the team, so it asks for it: nothing else loads a
        // trail's team until its card is opened, and the chip would otherwise
        // say "make or join a team" to somebody already on one.
        if (teams != null && !teams.Busy && teams.World != trails.SelectedId)
            teams.Load(trails.SelectedId);
        TeamService.Detail team = teams != null ? teams.Team : null;

        if (team == null || !team.on_team || string.IsNullOrEmpty(team.name))
        {
            label.text = AppStrings.Get("chip.noTeam");
            return;
        }

        int rank = 0;
        if (teams.Board != null)
        {
            for (int i = 0; i < teams.Board.Count; i++)
            {
                if (teams.Board[i].is_mine)
                {
                    rank = teams.Board[i].rank;
                    break;
                }
            }
        }

        label.text = rank > 0
            ? string.Format(AppStrings.Get("chip.team"), team.name, Ordinal(rank))
            : team.name;
    }

    /// <summary>1-во, 2-ро, 3-то, 4-то... in Bulgarian; 1st, 2nd, 3rd in English.</summary>
    public static string Ordinal(int n)
    {
        if (AppStrings.CurrentLanguage == AppLanguage.Bulgarian)
        {
            int last = n % 10, lastTwo = n % 100;
            string end = lastTwo >= 11 && lastTwo <= 13 ? "-то"
                       : last == 1 ? "-во" : last == 2 ? "-ро" : last == 7 || last == 8 ? "-мо" : "-то";
            return n + end;
        }

        int t = n % 100;
        string suffix = t >= 11 && t <= 13 ? "th" : (n % 10) == 1 ? "st" : (n % 10) == 2 ? "nd" : (n % 10) == 3 ? "rd" : "th";
        return n + suffix;
    }
}
