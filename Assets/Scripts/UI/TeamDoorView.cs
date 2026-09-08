using System.Collections;
using System.Collections.Generic;
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
    // Fifty-odd people at a launch, in teams the creator sizes: about twenty
    // teams on the board, and twelve is the largest team the database allows.
    // Both lists scroll, so these are the ceiling rather than what is drawn.
    public const int MaxRows = 24;
    public const int MaxMembers = 12;

    [Header("No team yet")]
    public GameObject noTeamGroup;

    [Tooltip("The two lines above the name box. They say something different " +
             "when a game is waiting for a team than when somebody simply has " +
             "not made one.")]
    public TextMeshProUGUI noneTitle;
    public TextMeshProUGUI noneBody;
    public TMP_InputField nameField;
    public Button createButton;
    public TMP_InputField codeField;
    public Button joinButton;

    [Header("On a team")]
    public GameObject teamGroup;

    [Tooltip("The half that sits under the board - the team's own name, who is " +
             "on it, and Leave. Shown and hidden with teamGroup.")]
    public GameObject teamGroupTail;
    public TextMeshProUGUI teamName;
    public TextMeshProUGUI teamMeta;
    public TextMeshProUGUI inviteCode;
    public Button leaveButton;

    [Tooltip("One row per member, built once. Extra rows are switched off.")]
    public TextMeshProUGUI[] memberRows = new TextMeshProUGUI[MaxMembers];

    [Tooltip("Says how many a team here holds, which is the trail's number and " +
             "not this app's.")]
    public TextMeshProUGUI capacityLabel;

    [Tooltip("What a moderator did to this team, and why. Switched off when " +
             "nobody has touched them, which is nearly always.")]
    public GameObject movedGroup;
    public TextMeshProUGUI movedTitle;
    public TextMeshProUGUI movedBody;

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

        // On a game with no team there is no way out of this card except making
        // one or joining one, because there is nothing useful to do behind it:
        // the server will not score a game for somebody who is on no team.
        bool gated = !onTeam && TrailService.Instance != null
                     && TrailService.Instance.SelectedIsGame;

        if (closeButton != null)
            closeButton.gameObject.SetActive(!gated);

        if (noTeamGroup != null)
            noTeamGroup.SetActive(!onTeam);

        // Before any of that: this trail may not take anonymous walkers at all,
        // and being told so here beats making a team name, pressing the button
        // and being refused by the server.
        bool needsAccount = TrailService.Instance != null
                            && TrailService.Instance.SelectedNeedsAccount
                            && SupabaseAuth.Instance != null
                            && SupabaseAuth.Instance.IsAnonymous;

        // Taken away, not greyed out.
        //
        // Two reasons. There is nothing to do with a team name on a trail that
        // will not take this identity, so a form there is furniture. And a
        // Selectable's disabled look is a timed colour transition, which is
        // exactly the thing that made the Check button on the question screen
        // photograph as live when it was not - a control that is off should not
        // depend on an animation to say so.
        if (createButton != null)
            createButton.gameObject.SetActive(!needsAccount);

        if (joinButton != null)
            joinButton.gameObject.SetActive(!needsAccount);

        if (nameField != null)
            nameField.gameObject.SetActive(!needsAccount);

        if (codeField != null)
            codeField.gameObject.SetActive(!needsAccount);

        // Gated, the card is a question rather than an observation about being
        // by yourself, so it asks one.
        if (noneTitle != null)
        {
            noneTitle.text = AppStrings.Get(
                needsAccount ? "team.account" : gated ? "team.needed" : "team.none");
        }

        if (noneBody != null && needsAccount)
            noneBody.text = AppStrings.Get("team.accountBody");
        else if (noneBody != null)
            noneBody.text = AppStrings.Get(gated ? "team.neededBody" : "team.noneBody");

        if (teamGroup != null)
            teamGroup.SetActive(onTeam);

        // The board sits between the two halves, so being on a team switches
        // both of them rather than one block.
        if (teamGroupTail != null)
            teamGroupTail.SetActive(onTeam);

        if (onTeam)
        {
            if (teamName != null)
                teamName.text = team.name;

            if (teamMeta != null)
            {
                teamMeta.text = string.Format(AppStrings.Get("team.meta"),
                                              team.solved, team.points);
            }

            DrawMoved(team);

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

        if (capacityLabel != null)
        {
            int cap = team != null ? team.Cap : 4;

            capacityLabel.text = onTeam
                ? string.Format(AppStrings.Get("team.capacity"), team.Size, cap)
                : string.Format(AppStrings.Get("team.capacityNone"), cap);
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

    /// <summary>
    /// What a moderator did to this team, on the team's own card.
    ///
    /// Being disqualified is said here too, and not because it was asked for.
    /// A disqualified team opens this card, looks at the standings beside it,
    /// and cannot find themselves. Without a line saying why, that reads as the
    /// app being broken - and on the day it turns into an argument about
    /// whether the phone is lying, which the phone would lose.
    /// </summary>
    void DrawMoved(TeamService.Detail team)
    {
        if (movedGroup == null)
            return;

        bool anything = team != null && (team.Moved || team.disqualified);
        movedGroup.SetActive(anything);

        if (!anything)
            return;

        if (movedTitle != null)
        {
            movedTitle.text = AppStrings.Get(team.disqualified ? "team.out" : "team.moved");
            movedTitle.color = ThemeManager.Get(ThemeRole.StatusPoor);
        }

        if (movedBody == null)
            return;

        List<string> lines = new List<string>();

        if (team.disqualified && !string.IsNullOrEmpty(team.disqualified_reason))
            lines.Add(team.disqualified_reason);

        if (team.Moved)
        {
            for (int i = 0; i < team.adjustments.Length; i++)
            {
                TeamService.Adjustment moved = team.adjustments[i];

                // The sign is written out, because "20" and "-20" one under the
                // other differ by one character and mean opposite things.
                string amount = (moved.points > 0 ? "+" : "") + moved.points;
                lines.Add(string.Format(AppStrings.Get("team.movedRow"), amount, moved.reason));
            }

            // The arithmetic, so the number at the top of the card is one they
            // can check rather than one they have to accept.
            lines.Add(string.Format(AppStrings.Get("team.earned"),
                                    team.earned, team.points - team.earned));
        }

        movedBody.text = string.Join("\n", lines.ToArray());
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
