using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// How the walk is going: stops found, points scored, and where the team stands.
///
/// Updates live rather than only on tab open, so a stop found from the Camera
/// tab is already reflected by the time the reader switches over - and so are
/// points, which are scored on a different screen entirely.
/// </summary>
public class DiscoveryProgressCard : MonoBehaviour
{
    [Header("References")]
    public TextMeshProUGUI countText;
    public TextMeshProUGUI labelText;
    public RectTransform barFill;

    [Tooltip("Explains what counts as explored, so the number isn't mysterious.")]
    public TextMeshProUGUI noteText;

    [Header("The rest of the walk")]
    [Tooltip("Points and standing. Hidden until there is a team to read them off, " +
             "which is the moment the walker first answers something.")]
    public GameObject statsRow;
    public TextMeshProUGUI pointsText;
    public TextMeshProUGUI pointsLabel;
    public TextMeshProUGUI standingText;
    public TextMeshProUGUI standingLabel;

    void OnEnable()
    {
        DiscoveryTracker.OnDiscoveryChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        BuildingDatabase.OnBuildingsChanged += Refresh;
        TeamService.OnTeamChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        DiscoveryTracker.OnDiscoveryChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        BuildingDatabase.OnBuildingsChanged -= Refresh;
        TeamService.OnTeamChanged -= Refresh;
    }

    public void Refresh()
    {
        int discovered = DiscoveryTracker.DiscoveredCount;
        int total = DiscoveryTracker.TotalCount;

        if (countText != null)
            countText.text = discovered + " / " + total;

        if (labelText != null)
            labelText.text = AppStrings.Get("search.discovered");

        if (noteText != null)
            noteText.text = AppStrings.Get("search.discoveredNote");

        if (barFill != null)
        {
            // An empty project would otherwise divide by zero on first run.
            float fraction = total > 0 ? (float)discovered / total : 0f;

            barFill.anchorMin = new Vector2(0f, 0f);
            barFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            barFill.offsetMin = Vector2.zero;
            barFill.offsetMax = Vector2.zero;
        }

        DrawStanding();
    }

    /// <summary>
    /// Points and where the team sits, when there is a team to ask about.
    ///
    /// ensure_team makes a team of one the first time anybody answers anything,
    /// so "no team" here means "has not answered anything yet" rather than
    /// "walking alone" - and a row of zeroes before the first question is worse
    /// than no row at all.
    /// </summary>
    void DrawStanding()
    {
        TeamService teams = TeamService.Instance;
        TeamService.Detail team = teams != null ? teams.Team : null;
        TrailService trails = TrailService.Instance;

        // A walk scores nothing, and ensure_team still makes a silent team of
        // one to hang progress on - so without the mode test this card would
        // show a guide's walker a rank and a total of zero, both meaningless.
        bool scored = trails != null && trails.SelectedIsGame;
        bool onTeam = scored && team != null && team.on_team;

        if (statsRow != null)
            statsRow.SetActive(onTeam);

        if (!onTeam)
            return;

        if (pointsText != null)
            pointsText.text = team.points.ToString();

        if (pointsLabel != null)
            pointsLabel.text = AppStrings.Get("search.points");

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

        if (standingText != null)
        {
            standingText.text = rank > 0
                ? string.Format(AppStrings.Get("search.rank"), rank)
                : "\u2014";
        }

        if (standingLabel != null)
        {
            standingLabel.text = AppStrings.Get(
                rank > 0 ? "search.standing" : "search.noTeam");
        }
    }
}
