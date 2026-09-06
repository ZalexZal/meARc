using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The team screen: who you are walking with, and where everybody stands.
///
/// Three states in one card, because they are one question - "who am I playing
/// with?" - and the answer is either a team, or an offer to make one, or an
/// admission that we could not ask.
///
/// Rows are built once and shown or hidden rather than instantiated from a
/// prefab. A team holds four people and a board on a school trail holds a dozen;
/// pooling twelve rows is less machinery than a prefab and a spawner, and it
/// cannot leak them.
/// </summary>
public class TeamDoorView : MonoBehaviour
{
    public const int MaxRows = 12;
    public const int MaxMembers = 4;

    [Header("No team yet")]
    public GameObject noTeamGroup;
    public TMP_InputField nameField;
    public Button createButton;
    public TMP_InputField codeField;
    public Button joinButton;

    [Header("On a team")]
    public GameObject teamGroup;
    public TextMeshProUGUI teamName;
    public TextMeshProUGUI teamMeta;
    public TextMeshProUGUI inviteCode;
    public Button leaveButton;

    [Tooltip("One row per member, built once. Extra rows are switched off.")]
    public TextMeshProUGUI[] memberRows = new TextMeshProUGUI[MaxMembers];

    [Header("The board")]
    public GameObject boardEmpty;
    public RectTransform[] boardRows = new RectTransform[MaxRows];
    public TextMeshProUGUI[] boardRank = new TextMeshProUGUI[MaxRows];
    public TextMeshProUGUI[] boardName = new TextMeshProUGUI[MaxRows];
    public TextMeshProUGUI[] boardPoints = new TextMeshProUGUI[MaxRows];
    public Image[] boardHighlight = new Image[MaxRows];

    [Header("Both")]
    public TextMeshProUGUI statusLabel;
    public Button closeButton;

    void OnEnable()
    {
        if (createButton != null)
        {
            createButton.onClick.RemoveAllListeners();
            createButton.onClick.AddListener(Create);
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(Join);
        }

        if (leaveButton != null)
        {
            leaveButton.onClick.RemoveAllListeners();
            leaveButton.onClick.AddListener(Leave);
        }

        TeamService.OnTeamChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;

        // A teammate's solve is written by check_answer on their phone, so this
        // one only learns about it by asking. Realtime would push it instead and
        // is the better answer, but it means a WebSocket and the Phoenix channel
        // protocol written from scratch, on a persistent connection over a mobile
        // network, with no device here to try it on. Fifteen seconds is a few
        // seconds of staleness nobody in a room will notice.
        MarkerTaskService.OnProgressChanged += Reload;
        _polling = StartCoroutine(Poll());

        // Asked every time it opens. A board that is one walk out of date is
        // worse than a board that took a moment to arrive.
        TrailService trails = TrailService.Instance;
        TeamService teams = TeamService.Instance;

        if (teams != null && trails != null && !string.IsNullOrEmpty(trails.SelectedId))
            teams.Load(trails.SelectedId);

        Refresh();
    }

    void OnDisable()
    {
        TeamService.OnTeamChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        MarkerTaskService.OnProgressChanged -= Reload;

        // Stopped with the card. A poll that outlives the screen it feeds is a
        // request every fifteen seconds for the rest of the walk.
        if (_polling != null)
        {
            StopCoroutine(_polling);
            _polling = null;
        }
    }

    Coroutine _polling;

    /// <summary>How often the standings are re-read while this card is open.</summary>
    public float pollSeconds = 15f;

    IEnumerator Poll()
    {
        while (true)
        {
            yield return new WaitForSeconds(pollSeconds);
            Reload();
        }
    }

    /// <summary>
    /// Asks again. Also wired to a finished answer, because the moment somebody
    /// most wants to see the board move is the moment they just scored.
    /// </summary>
    void Reload()
    {
        TeamService teams = TeamService.Instance;
        TrailService trails = TrailService.Instance;

        if (teams == null || teams.Busy || trails == null || string.IsNullOrEmpty(trails.SelectedId))
            return;

        teams.Load(trails.SelectedId);
    }

    public void Refresh()
    {
        TeamService teams = TeamService.Instance;
        TeamService.Detail team = teams != null ? teams.Team : null;
        bool onTeam = team != null && team.on_team;

        if (noTeamGroup != null)
            noTeamGroup.SetActive(!onTeam);

        if (teamGroup != null)
            teamGroup.SetActive(onTeam);

        if (onTeam)
        {
            if (teamName != null)
                teamName.text = team.name;

            if (teamMeta != null)
            {
                teamMeta.text = string.Format(AppStrings.Get("team.meta"),
                                              team.solved, team.points);
            }

            if (inviteCode != null)
                inviteCode.text = team.invite_code;

            for (int i = 0; i < memberRows.Length; i++)
            {
                if (memberRows[i] == null)
                    continue;

                bool used = team.members != null && i < team.members.Length;
                memberRows[i].gameObject.SetActive(used);

                if (!used)
                    continue;

                TeamService.Member member = team.members[i];

                // "you" beside your own row, because on a four-person board every
                // name looks equally like somebody else.
                memberRows[i].text = member.is_me
                    ? member.name + "  " + AppStrings.Get("team.you")
                    : member.name;
            }

            if (createButton != null)
                createButton.interactable = false;
        }

        DrawBoard(teams);

        if (statusLabel != null)
        {
            string error = teams != null ? teams.LastError : null;

            statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(error));

            if (!string.IsNullOrEmpty(error))
            {
                statusLabel.text = error.StartsWith("team.")
                    ? AppStrings.Get(error)
                    : error;
                statusLabel.color = ThemeManager.Get(ThemeRole.StatusPoor);
            }
        }
    }

    void DrawBoard(TeamService teams)
    {
        int count = teams != null && teams.Board != null ? teams.Board.Count : 0;

        if (boardEmpty != null)
            boardEmpty.SetActive(count == 0);

        for (int i = 0; i < boardRows.Length; i++)
        {
            if (boardRows[i] == null)
                continue;

            bool used = i < count;
            boardRows[i].gameObject.SetActive(used);

            if (!used)
                continue;

            TeamService.Standing row = teams.Board[i];

            if (boardRank[i] != null)
                boardRank[i].text = row.rank.ToString();

            if (boardName[i] != null)
                boardName[i].text = row.team_name;

            if (boardPoints[i] != null)
                boardPoints[i].text = row.points.ToString();

            // Your own row, marked by a wash rather than a different text colour:
            // the board is read at a glance from across a room.
            if (boardHighlight[i] != null)
            {
                boardHighlight[i].gameObject.SetActive(row.is_mine);

                Color accent = ThemeManager.Get(ThemeRole.Accent);
                accent.a = 0.14f;
                boardHighlight[i].color = accent;
            }
        }
    }

    void Create()
    {
        TeamService teams = TeamService.Instance;
        if (teams == null || teams.Busy)
            return;

        string name = nameField != null ? nameField.text.Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            Say("team.needName");
            return;
        }

        Say("team.working");
        teams.Create(name, Done);
    }

    void Join()
    {
        TeamService teams = TeamService.Instance;
        if (teams == null || teams.Busy)
            return;

        string code = codeField != null ? codeField.text.Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            Say("team.needCode");
            return;
        }

        Say("team.working");
        teams.Join(code, Done);
    }

    void Leave()
    {
        TeamService teams = TeamService.Instance;
        if (teams == null || teams.Busy)
            return;

        Say("team.working");
        teams.Leave(Done);
    }

    void Done(string error)
    {
        if (string.IsNullOrEmpty(error))
        {
            if (nameField != null)
                nameField.text = string.Empty;

            if (codeField != null)
                codeField.text = string.Empty;

            Say(null);
            Refresh();
            return;
        }

        if (statusLabel == null)
            return;

        statusLabel.text = error.StartsWith("team.") ? AppStrings.Get(error) : error;
        statusLabel.color = ThemeManager.Get(ThemeRole.StatusPoor);
        statusLabel.gameObject.SetActive(true);
    }

    void Say(string key)
    {
        if (statusLabel == null)
            return;

        if (string.IsNullOrEmpty(key))
        {
            statusLabel.text = string.Empty;
            statusLabel.gameObject.SetActive(false);
            return;
        }

        statusLabel.text = AppStrings.Get(key);
        statusLabel.color = ThemeManager.Get(ThemeRole.TextMuted);
        statusLabel.gameObject.SetActive(true);
    }
}
