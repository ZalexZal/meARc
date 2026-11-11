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
    public const int CodeLength = 6;

    /// <summary>
    /// A team code as it is drawn: one character per cell, each centred.
    ///
    /// It is read out across a room, so every character has to sit the same
    /// distance from the next. Tracking left a hole after a digit (Inter's
    /// digits are tabular, its letters are not: "GAAT5 N"), and TMP's mspace
    /// left-aligns each glyph in its slot, so narrow letters floated and a W
    /// crowded its neighbours ("S T X YWK"). Cells are what the trail code's
    /// six boxes already do.
    /// </summary>
    public static void ShowCode(TextMeshProUGUI[] cells, string code)
    {
        if (cells == null)
            return;

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null)
                cells[i].text = code != null && i < code.Length ? code[i].ToString() : string.Empty;
        }
    }

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

    [Tooltip("Shown instead of the form when this trail will not take an anonymous " +
             "walker. Opens the account form rather than describing where it is.")]
    public Button accountButton;
    public TMP_InputField codeField;
    public Button joinButton;

    [Header("On a team")]
    public GameObject teamGroup;

    [Tooltip("The half that sits under the board - the team's own name, who is " +
             "on it, and Leave. Shown and hidden with teamGroup.")]
    public GameObject teamGroupTail;
    public TextMeshProUGUI teamName;
    public TextMeshProUGUI teamMeta;
    public TextMeshProUGUI[] inviteCells = new TextMeshProUGUI[CodeLength];
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

    [Tooltip("Each row, as a button. Tapping another team's row opens the report " +
             "sheet for its name, which is also where it can be blocked.")]
    public Button[] boardButtons = new Button[MaxRows];
    public ReportSheetView reportSheet;

    [Header("Both")]
    public TextMeshProUGUI statusLabel;
    public Button closeButton;

    [Header("Daylight v2")]
    [Tooltip("Copies the team code; the code is what people read out across a room.")]
    public Button copyButton;

    [Tooltip("Hands the code to the phone's share sheet.")]
    public Button shareButton;

    [Tooltip("The Leave button's face: clear until armed, then the warning red.")]
    public Image leaveFace;

    [Header("The captain (0058)")]
    [Tooltip("Each member row, as a button: the captain taps a teammate to act on them.")]
    public Button[] memberButtons = new Button[MaxMembers];

    [Tooltip("Shown under the names once the captain has tapped a teammate.")]
    public GameObject captainGroup;
    public TextMeshProUGUI captainLabel;
    public Button makeCaptainButton;
    public Button removeButton;

    [Tooltip("Only the captain sees it; armed by the first tap, like Leave.")]
    public Button giveUpButton;
    public Image giveUpFace;

    string _chosenId;
    bool _removeArmed, _giveUpArmed;
    float _armedAt;

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

        if (copyButton != null)
        {
            copyButton.onClick.RemoveAllListeners();
            copyButton.onClick.AddListener(CopyCode);
        }

        for (int i = 0; i < memberButtons.Length; i++)
        {
            if (memberButtons[i] == null)
                continue;
            int row = i;
            memberButtons[i].onClick.RemoveAllListeners();
            memberButtons[i].onClick.AddListener(() => Choose(row));
        }

        if (makeCaptainButton != null)
        {
            makeCaptainButton.onClick.RemoveAllListeners();
            makeCaptainButton.onClick.AddListener(MakeCaptain);
        }

        if (removeButton != null)
        {
            removeButton.onClick.RemoveAllListeners();
            removeButton.onClick.AddListener(Remove);
        }

        if (giveUpButton != null)
        {
            giveUpButton.onClick.RemoveAllListeners();
            giveUpButton.onClick.AddListener(GiveUp);
        }

        if (shareButton != null)
        {
            shareButton.onClick.RemoveAllListeners();
            shareButton.onClick.AddListener(ShareCode);
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
        ReportSheetView.OnBlocked += Reload;
        _polling = StartCoroutine(Poll());

        // Asked every time it opens. A board that is one walk out of date is
        // worse than a board that took a moment to arrive.
        TrailService trails = TrailService.Instance;
        TeamService teams = TeamService.Instance;

        if (teams != null && trails != null && !string.IsNullOrEmpty(trails.SelectedId))
            teams.Load(trails.SelectedId);

        // Whether this trail takes anonymous walkers decides which half of the
        // card is offered - the form, or the way to an account - and on a phone
        // it arrived false on at least one launch path because nothing had asked.
        // So the card asks, whenever it opens without an answer for this trail,
        // and draws again when the answer comes.
        if (trails != null && !string.IsNullOrEmpty(trails.SelectedId) &&
            trails.SummaryFor != trails.SelectedId)
        {
            trails.LoadNeedsAccount(trails.SelectedId, Refresh);
        }

        Refresh();
    }

    void OnDisable()
    {
        _leaveArmed = false;
        _removeArmed = _giveUpArmed = false;
        _chosenId = null;
        DrawLeave();
        TeamService.OnTeamChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        MarkerTaskService.OnProgressChanged -= Reload;
        ReportSheetView.OnBlocked -= Reload;

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
        // Leave's words are this view's own (it rewrites them as it arms), so
        // they follow the language from here rather than from a localiser.
        DrawLeave();
        DrawCaptain();

        TeamService teams = TeamService.Instance;
        TeamService.Detail team = teams != null ? teams.Team : null;
        bool onTeam = team != null && team.on_team;

        // Gated only in what it says, never in whether you can leave.
        //
        // Close used to be taken away on a game with no team, on the reasoning
        // that there is nothing to score behind the card until you are on one.
        // That is a locked door with the reader inside it: somebody who wants to
        // look at the map first, wait for a friend's code, or simply read the
        // stops could not do any of it. The server still refuses to score them,
        // in its own words, and the Team button on the map brings this back.
        bool gated = !onTeam && TrailService.Instance != null
                     && TrailService.Instance.SelectedIsGame;

        if (closeButton != null)
            closeButton.gameObject.SetActive(true);

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

        if (accountButton != null)
            accountButton.gameObject.SetActive(needsAccount);

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
                // "1 stops" is the kind of thing a room of teenagers reads out
                // loud. One stop gets its own sentence, in both languages.
                // Who is in it first, as Daylight's caption under the name has it.
                teamMeta.text = string.Format(AppStrings.Get("team.capacity"), team.Size, team.Cap) +
                                " · " +
                                string.Format(
                                    AppStrings.Get(team.solved == 1 ? "team.metaOne" : "team.meta"),
                                    team.solved, team.points);
            }

            DrawMoved(team);

            ShowCode(inviteCells, team.invite_code);

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
                // name looks equally like somebody else; then the captain's star
                // and whether their location works (0058).
                string line = member.is_me
                    ? member.name + "  " + AppStrings.Get("team.you")
                    : member.name;
                if (member.is_captain)
                    line += "  \u2605 " + AppStrings.Get("team.captainTag");
                if (member.gps)
                    line += "  \u00b7 " + AppStrings.Get("team.gpsTag");
                memberRows[i].text = line;

                // Only the captain's taps on a teammate do anything.
                if (i < memberButtons.Length && memberButtons[i] != null)
                    memberButtons[i].interactable = team.i_am_captain && !member.is_me && !team.gave_up;
            }

            if (team.gave_up && teamMeta != null)
                teamMeta.text = AppStrings.Get("team.gaveUp");

        }

        // Both ways, every time.
        //
        // This was only ever switched off - on the branch that draws a team you
        // are already on - and nothing switched it back. So anybody who had been
        // on a team in this session and then left it, or whose card first drew
        // while their team was still loading, got the form back with Make a team
        // dimmed and dead however much they typed into the name.
        if (createButton != null)
            createButton.interactable = !onTeam;

        // Tells the log what the card believes. The Make-a-team button has been
        // reported dead after leaving a team, with nothing else in the log to
        // say why - this is the line that says why.
        if (createButton != null)
        {
            CanvasGroup g = createButton.GetComponent<CanvasGroup>();
            Debug.Log("[Team] card onTeam=" + onTeam +
                      " create.active=" + createButton.gameObject.activeInHierarchy +
                      " interactable=" + createButton.interactable +
                      " isInteractable=" + createButton.IsInteractable() +
                      " alpha=" + (g != null ? g.alpha.ToString("0.00") : "-") +
                      " busy=" + (teams != null && teams.Busy));
        }

        if (capacityLabel != null)
        {
            // On a team it is in the line under the name instead; above the
            // name it read as the sheet's heading.
            capacityLabel.gameObject.SetActive(!onTeam);

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
                statusLabel.text = Words(error);
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

            // No name means its founder is somebody this reader blocked. The row
            // stays, because the ranks under it are still true.
            if (boardName[i] != null)
                boardName[i].text = string.IsNullOrEmpty(row.team_name)
                    ? AppStrings.Get("board.hidden")
                    : row.team_name;

            if (boardButtons[i] != null)
            {
                string teamId = row.team_id;
                boardButtons[i].onClick.RemoveAllListeners();
                boardButtons[i].interactable = !row.is_mine && reportSheet != null &&
                                               !string.IsNullOrEmpty(row.team_name);

                if (boardButtons[i].interactable)
                    boardButtons[i].onClick.AddListener(delegate { reportSheet.OpenTeam(teamId); });
            }

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
        if (teams == null)
            return;

        // Only another change in flight holds this back, and then it says so
        // rather than doing nothing.
        if (teams.Writing)
        {
            Say("team.working");
            return;
        }

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
        if (teams == null)
            return;

        if (teams.Writing)
        {
            Say("team.working");
            return;
        }

        string code = codeField != null ? codeField.text.Trim() : string.Empty;

        if (string.IsNullOrWhiteSpace(code))
        {
            Say("team.needCode");
            return;
        }

        Say("team.working");
        teams.Join(code, Done);
    }

    bool _leaveArmed;
    float _leaveArmedAt;

    void Update()
    {
        // An armed Leave left alone goes quiet again, like the trail list's.
        if (_leaveArmed && Time.unscaledTime - _leaveArmedAt > 4f)
        {
            _leaveArmed = false;
            DrawLeave();
        }

        // And so do Remove and Give up.
        if ((_removeArmed || _giveUpArmed) && Time.unscaledTime - _armedAt > 4f)
        {
            _removeArmed = _giveUpArmed = false;
            DrawCaptain();
        }
    }

    // ------------------------------------------------------- the captain (0058)

    TeamService.Member Chosen
    {
        get
        {
            TeamService.Detail team = TeamService.Instance != null ? TeamService.Instance.Team : null;
            if (team == null || team.members == null || string.IsNullOrEmpty(_chosenId))
                return null;
            foreach (TeamService.Member m in team.members)
                if (m != null && m.id == _chosenId)
                    return m;
            return null;
        }
    }

    /// <summary>The captain taps a teammate: what can be done to them appears.</summary>
    void Choose(int row)
    {
        TeamService.Detail team = TeamService.Instance != null ? TeamService.Instance.Team : null;
        if (team == null || !team.i_am_captain || team.members == null || row >= team.members.Length)
            return;
        TeamService.Member m = team.members[row];
        if (m == null || m.is_me)
            return;
        _chosenId = _chosenId == m.id ? null : m.id;
        _removeArmed = false;
        Haptics.Tick();
        DrawCaptain();
    }

    void DrawCaptain()
    {
        TeamService.Detail team = TeamService.Instance != null ? TeamService.Instance.Team : null;
        bool captain = team != null && team.on_team && team.i_am_captain && !team.gave_up;
        TeamService.Member chosen = captain ? Chosen : null;

        if (captainGroup != null)
            captainGroup.SetActive(chosen != null);
        if (captainLabel != null && chosen != null)
            captainLabel.text = chosen.name;

        SetLabel(removeButton, _removeArmed ? "team.removeConfirm" : "team.remove");

        if (giveUpButton != null)
        {
            giveUpButton.gameObject.SetActive(captain);
            SetLabel(giveUpButton, _giveUpArmed ? "team.giveUpConfirm" : "team.giveUp");
            TextMeshProUGUI label = giveUpButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
                label.color = _giveUpArmed ? Color.white : ThemeManager.Get(ThemeRole.StatusPoor);
            if (giveUpFace != null)
            {
                Color red = ThemeManager.Get(ThemeRole.BadDot);
                red.a = _giveUpArmed ? 1f : 0f;
                giveUpFace.color = red;
            }
        }
    }

    static void SetLabel(Button button, string key)
    {
        TextMeshProUGUI label = button != null ? button.GetComponentInChildren<TextMeshProUGUI>() : null;
        if (label != null)
            label.text = AppStrings.Get(key);
    }

    void MakeCaptain()
    {
        TeamService.Member m = Chosen;
        if (m == null || TeamService.Instance == null)
            return;
        _chosenId = null;
        Say("team.working");
        TeamService.Instance.TransferCaptain(m.id, Done);
    }

    void Remove()
    {
        TeamService.Member m = Chosen;
        if (m == null || TeamService.Instance == null)
            return;
        if (!_removeArmed)
        {
            _removeArmed = true;
            _armedAt = Time.unscaledTime;
            DrawCaptain();
            return;
        }
        _removeArmed = false;
        _chosenId = null;
        Say("team.working");
        TeamService.Instance.RemoveMember(m.id, Done);
    }

    void GiveUp()
    {
        if (TeamService.Instance == null)
            return;
        if (!_giveUpArmed)
        {
            _giveUpArmed = true;
            _armedAt = Time.unscaledTime;
            DrawCaptain();
            return;
        }
        _giveUpArmed = false;
        Say("team.working");
        TeamService.Instance.GiveUp(Done);
    }

    /// <summary>
    /// First tap asks, second leaves. The last one out takes the team with
    /// them - leave_team deletes an empty team and its score - and a single tap
    /// a centimetre from Close was enough to lose a finished trail's points.
    /// </summary>
    void DrawLeave()
    {
        TextMeshProUGUI label = leaveButton != null
            ? leaveButton.GetComponentInChildren<TextMeshProUGUI>() : null;
        if (label == null)
            return;

        TeamService.Detail team = TeamService.Instance != null ? TeamService.Instance.Team : null;
        bool last = team != null && (team.members == null || team.members.Length <= 1);

        label.text = !_leaveArmed ? AppStrings.Get("team.leave")
                   : last ? string.Format(AppStrings.Get("team.leaveLast"), team.points)
                   : AppStrings.Get("team.leaveConfirm");

        // Red words while it only asks; a red button once it means it.
        label.color = _leaveArmed ? Color.white : ThemeManager.Get(ThemeRole.StatusPoor);

        if (leaveFace != null)
        {
            Color red = ThemeManager.Get(ThemeRole.BadDot);
            red.a = _leaveArmed ? 1f : 0f;
            leaveFace.color = red;
        }
    }

    string Code
    {
        get
        {
            TeamService.Detail team = TeamService.Instance != null ? TeamService.Instance.Team : null;
            return team != null ? team.invite_code : null;
        }
    }

    void CopyCode()
    {
        string code = Code;
        if (string.IsNullOrEmpty(code))
            return;

        GUIUtility.systemCopyBuffer = code;
        Haptics.Tick();

        // Android 13 and later say "Copied" themselves.
        if (Haptics.Sdk < 33)
            ProximityToast.Say(AppStrings.Get("team.copied"));
    }

    void ShareCode()
    {
        string code = Code;
        if (string.IsNullOrEmpty(code))
            return;

        string text = string.Format(AppStrings.Get("team.shareText"), code);

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaObject intent = new AndroidJavaObject("android.content.Intent"))
            using (AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent"))
            using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                intent.Call<AndroidJavaObject>("setAction", "android.intent.action.SEND");
                intent.Call<AndroidJavaObject>("setType", "text/plain");
                intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", text);

                using (AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>(
                           "createChooser", intent, AppStrings.Get("team.share")))
                {
                    activity.Call("startActivity", chooser);
                }
            }
            return;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[Team] Share sheet failed: " + exception.Message);
        }
#endif
        GUIUtility.systemCopyBuffer = text;
        ProximityToast.Say(AppStrings.Get("team.copied"));
    }

    void Leave()
    {
        TeamService teams = TeamService.Instance;
        if (teams == null)
            return;

        if (teams.Writing)
        {
            Say("team.working");
            return;
        }

        if (!_leaveArmed)
        {
            _leaveArmed = true;
            _leaveArmedAt = Time.unscaledTime;
            DrawLeave();
            return;
        }

        _leaveArmed = false;
        DrawLeave();
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

        statusLabel.text = Words(error);
        statusLabel.color = ThemeManager.Get(ThemeRole.StatusPoor);
        statusLabel.gameObject.SetActive(true);
    }

    /// <summary>
    /// The server's refusals in the reader's language. They came through as
    /// raw lowercase English - "no team with that code" - on both languages.
    /// </summary>
    static string Words(string error)
    {
        if (error.StartsWith("team."))
            return AppStrings.Get(error);

        string e = error.ToLowerInvariant();
        string key = e.Contains("no team with that code") ? "team.err.unknown"
                   : e.Contains("team is full") ? "team.err.full"
                   : e == "banned" || e.Contains("\"message\":\"banned\"") ? "teacher.err.banned"
                   : e.Contains("already on a team") || e.Contains("leave your team") ? "team.err.already"
                   : e.Contains("a guide, not a game") ? "team.err.guide"
                   : e.Contains("hands over or gives up") ? "team.err.captain"
                   : e.Contains("given up") ? "team.err.gaveUp"
                   : null;

        return key != null ? AppStrings.Get(key) : error;
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
