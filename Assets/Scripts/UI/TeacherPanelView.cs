using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Учителски пулт": the organiser's screen for a trail they own.
///
/// One full-screen panel with two tabs - Lobby (every team and who is in it,
/// GPS dots, Kick and Ban per player, Disqualify per team, everybody in no
/// team) and Scoreboard (from world_teams, so every team shows from the
/// start at 0; world_leaderboard hides teams with nothing solved) - and a header with the trail's
/// start state and, for a manual start not yet given, Start.
///
/// This component sits on an always-active holder so <see cref="Open"/> can
/// find it; the panel itself is a child switched on and off. It asks the
/// server every five seconds, and only while the panel is up.
///
/// Rows are cloned at run time from three inactive templates the builder
/// makes (a team card, a player row, a board row) and reused, never destroyed:
/// a class of thirty is thirty rows, not a prefab and a spawner.
/// </summary>
public class TeacherPanelView : MonoBehaviour
{
    public static TeacherPanelView Instance { get; private set; }

    /// <summary>True while the panel is on screen. The waiting screen stays down then.</summary>
    public static bool IsOpen
    {
        get { return Instance != null && Instance.panel != null && Instance.panel.activeSelf; }
    }

    /// <summary>Raised when the panel opens or closes.</summary>
    public static event Action OnOpenChanged;

    public float pollSeconds = 5f;

    [Header("Frame")]
    public GameObject panel;
    public TextMeshProUGUI titleLabel;
    public TextMeshProUGUI stateLabel;
    public TextMeshProUGUI readyLabel;
    public Button startButton;
    public Button closeButton;

    [Header("Tabs")]
    public Button lobbyTab;
    public Button boardTab;
    public Image lobbyTabFace;
    public Image boardTabFace;
    public TextMeshProUGUI lobbyTabLabel;
    public TextMeshProUGUI boardTabLabel;
    public GameObject lobbyList;
    public GameObject boardList;
    public RectTransform lobbyContent;
    public RectTransform boardContent;

    [Header("Empty notes")]
    public GameObject lobbyEmpty;
    public TextMeshProUGUI lobbyEmptyTitle;
    public GameObject lobbyEmptyBody;
    public GameObject boardEmpty;

    [Header("Templates (inactive)")]
    [Tooltip("Head/Name, Head/Meta, Head/Disqualify (Button), Members (RectTransform).")]
    public GameObject teamTemplate;

    [Tooltip("Dot (Image), Name, Kick (Button), Ban (Button).")]
    public GameObject playerTemplate;

    [Tooltip("Rank, Name, Meta, Points.")]
    public GameObject boardTemplate;

    [Header("Confirmation")]
    public GameObject confirmGroup;
    public TextMeshProUGUI confirmTitle;
    public TextMeshProUGUI confirmBody;
    public TextMeshProUGUI confirmActionLabel;
    public Button confirmAction;
    public Button confirmCancel;

    // ------------------------------------------------------------------ state

    string _world;
    string _name;
    bool _showBoard;
    bool _now;
    bool _acting;
    bool _toldError;
    Coroutine _poll;
    Action _pending;

    TeacherService.StartState _start;
    List<TeacherService.TeamRow> _teams;
    List<TeacherService.Player> _players;
    readonly HashSet<string> _disqualified = new HashSet<string>();

    class PlayerRow
    {
        public GameObject go;
        public Image dot;
        public TextMeshProUGUI name;
        public Button kick;
        public Button ban;
        public string userId;
        public string who;
    }

    class TeamCard
    {
        public GameObject go;
        public TextMeshProUGUI name;
        public TextMeshProUGUI meta;
        public Button disqualify;
        public RectTransform members;
        public string teamId;
        public string teamName;
        public readonly List<PlayerRow> rows = new List<PlayerRow>();
    }

    class BoardRow
    {
        public GameObject go;
        public TextMeshProUGUI rank;
        public TextMeshProUGUI name;
        public TextMeshProUGUI meta;
        public TextMeshProUGUI points;
    }

    readonly List<TeamCard> _cards = new List<TeamCard>();
    readonly List<BoardRow> _boardRows = new List<BoardRow>();

    // -------------------------------------------------------------- lifetime

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;

        if (panel != null)
            panel.SetActive(false);
        if (confirmGroup != null)
            confirmGroup.SetActive(false);
        Hide(teamTemplate);
        Hide(playerTemplate);
        Hide(boardTemplate);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Draw;
        ThemeManager.OnThemeChanged += Draw;

        if (lobbyTab != null)
        {
            lobbyTab.onClick.RemoveAllListeners();
            lobbyTab.onClick.AddListener(ShowLobby);
        }

        if (boardTab != null)
        {
            boardTab.onClick.RemoveAllListeners();
            boardTab.onClick.AddListener(ShowBoard);
        }

        if (startButton != null)
        {
            startButton.onClick.RemoveAllListeners();
            startButton.onClick.AddListener(AskStart);
        }
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Draw;
        ThemeManager.OnThemeChanged -= Draw;
        StopPolling();
    }

    // ------------------------------------------------------------ public API

    /// <summary>
    /// Opens the panel for a trail the reader owns. The server decides whether
    /// they may moderate it; a refusal shows as a toast and an empty roster.
    /// </summary>
    public static void Open(string worldId, string trailName)
    {
        if (Instance == null)
        {
            Debug.LogWarning("[Teacher] No teacher panel in the scene.");
            return;
        }

        Instance.Show(worldId, trailName);
    }

    /// <summary>The trail the panel is showing, or null.</summary>
    public string WorldId { get { return _world; } }

    public void Show(string worldId, string trailName)
    {
        if (string.IsNullOrEmpty(worldId) || panel == null)
            return;

        if (worldId != _world)
        {
            _start = null;
            _teams = null;
            _players = null;
            _disqualified.Clear();
        }

        _world = worldId;
        _name = trailName;
        _toldError = false;
        _acting = false;
        _showBoard = false;

        HideConfirm();
        panel.SetActive(true);
        Draw();

        StopPolling();
        _poll = StartCoroutine(Poll());

        if (OnOpenChanged != null)
            OnOpenChanged();
    }

    /// <summary>The close control, and the phone's back button through WayOut.</summary>
    public void Close()
    {
        HideConfirm();
        StopPolling();

        if (panel != null)
            panel.SetActive(false);

        if (OnOpenChanged != null)
            OnOpenChanged();
    }

    public void ShowLobby()
    {
        _showBoard = false;
        Draw();
    }

    public void ShowBoard()
    {
        _showBoard = true;
        Draw();
    }

    // --------------------------------------------------------------- polling

    void StopPolling()
    {
        if (_poll != null)
        {
            StopCoroutine(_poll);
            _poll = null;
        }
    }

    IEnumerator Poll()
    {
        while (panel != null && panel.activeSelf)
        {
            yield return Load();

            float until = Time.unscaledTime + pollSeconds;
            while (!_now && Time.unscaledTime < until)
                yield return null;

            _now = false;
        }

        _poll = null;
    }

    IEnumerator Load()
    {
        string world = _world;
        string error = null;

        TeacherService.StartState start = null;
        yield return TeacherService.FetchStart(world, (s, e) => { start = s; });

        List<TeacherService.TeamRow> teams = null;
        yield return TeacherService.FetchTeams(world, (rows, e) => { teams = rows; error = e; });

        // Quiet on failure: a server without 0071 still has teams to show.
        List<TeacherService.Player> players = null;
        yield return TeacherService.FetchPlayers(world, (rows, e) => { players = rows; });

        if (world != _world || panel == null || !panel.activeSelf)
            yield break;

        if (start != null)
            _start = start;

        if (teams != null)
        {
            _teams = teams;
            _disqualified.Clear();
            for (int i = 0; i < teams.Count; i++)
                if (teams[i] != null && teams[i].Disqualified && !string.IsNullOrEmpty(teams[i].id))
                    _disqualified.Add(teams[i].id);
        }

        if (players != null)
            _players = players;

        // Said once per opening, not every five seconds.
        if (error != null && !_toldError)
        {
            _toldError = true;
            ProximityToast.Say(error);
        }

        Draw();
    }

    // --------------------------------------------------------------- actions

    void AskStart()
    {
        if (_acting || string.IsNullOrEmpty(_world))
            return;

        string world = _world;
        Ask(AppStrings.Get("teacher.startTitle"), AppStrings.Get("teacher.startBody"),
            AppStrings.Get("teacher.start"), () =>
            {
                Begin();
                StartCoroutine(TeacherService.StartGame(world, error =>
                {
                    Done(error, AppStrings.Get("teacher.started"));

                    // The organiser may have this trail open too: its stops exist now.
                    TrailService trails = TrailService.Instance;
                    if (error == null && trails != null && trails.SelectedId == world)
                        trails.LoadNeedsAccount(world);
                }));
            });
    }

    void AskKick(PlayerRow row)
    {
        if (_acting || row == null || string.IsNullOrEmpty(row.userId))
            return;

        string world = _world, id = row.userId, who = row.who;
        Ask(string.Format(AppStrings.Get("teacher.kickTitle"), who), AppStrings.Get("teacher.kickBody"),
            AppStrings.Get("teacher.kick"), () =>
            {
                Begin();
                StartCoroutine(TeacherService.Kick(world, id, error =>
                    Done(error, string.Format(AppStrings.Get("teacher.kicked"), who))));
            });
    }

    void AskBan(PlayerRow row)
    {
        if (_acting || row == null || string.IsNullOrEmpty(row.userId))
            return;

        string world = _world, id = row.userId, who = row.who;
        Ask(string.Format(AppStrings.Get("teacher.banTitle"), who), AppStrings.Get("teacher.banBody"),
            AppStrings.Get("teacher.ban"), () =>
            {
                Begin();
                StartCoroutine(TeacherService.Ban(world, id, error =>
                    Done(error, string.Format(AppStrings.Get("teacher.banned"), who))));
            });
    }

    void AskDisqualify(TeamCard card)
    {
        if (_acting || card == null || string.IsNullOrEmpty(card.teamId))
            return;

        string id = card.teamId, team = card.teamName;
        Ask(string.Format(AppStrings.Get("teacher.dqTitle"), team), AppStrings.Get("teacher.dqBody"),
            AppStrings.Get("teacher.dq"), () =>
            {
                Begin();
                StartCoroutine(TeacherService.Disqualify(id, AppStrings.Get("teacher.dqReason"), error =>
                {
                    // Off the board at once, before the next read confirms it.
                    if (error == null)
                        _disqualified.Add(id);
                    Done(error, string.Format(AppStrings.Get("teacher.disqualified"), team));
                }));
            });
    }

    void Begin()
    {
        _acting = true;
        Draw();
    }

    void Done(string error, string ok)
    {
        _acting = false;
        ProximityToast.Say(error ?? ok);
        _now = true;
        Draw();
    }

    // ---------------------------------------------------------- confirmation

    void Ask(string title, string body, string action, Action run)
    {
        if (confirmGroup == null)
            return;

        Set(confirmTitle, title);
        Set(confirmBody, body);
        Set(confirmActionLabel, action);
        _pending = run;
        confirmGroup.SetActive(true);
    }

    /// <summary>The red button on the confirmation card.</summary>
    public void ConfirmYes()
    {
        Action run = _pending;
        HideConfirm();
        if (run != null)
            run();
    }

    /// <summary>Cancel, a tap outside the card, or back.</summary>
    public void ConfirmNo()
    {
        HideConfirm();
    }

    void HideConfirm()
    {
        _pending = null;
        if (confirmGroup != null)
            confirmGroup.SetActive(false);
    }

    // ---------------------------------------------------------------- drawing

    static string Me
    {
        get { return SupabaseAuth.Instance != null ? SupabaseAuth.Instance.UserId : null; }
    }

    void Draw()
    {
        if (panel == null || !panel.activeSelf)
            return;

        Set(titleLabel, string.IsNullOrEmpty(_name) ? AppStrings.Get("teacher.title") : _name);
        Set(stateLabel, StateText(_start));

        if (startButton != null)
        {
            bool canStart = _start != null && _start.CanStart;
            if (startButton.gameObject.activeSelf != canStart)
                startButton.gameObject.SetActive(canStart);
            startButton.interactable = !_acting;
        }

        TeacherService.Lobby lobby = TeacherService.BuildLobby(_teams, _players, Me);

        if (readyLabel != null)
        {
            readyLabel.text = lobby.phones > 0
                ? string.Format(AppStrings.Get("teacher.ready"), lobby.ready, lobby.phones)
                : string.Empty;
            readyLabel.gameObject.SetActive(lobby.phones > 0);
        }

        DrawTabs();
        DrawLobby(lobby);
        DrawBoard();
    }

    void DrawTabs()
    {
        if (lobbyList != null && lobbyList.activeSelf == _showBoard)
            lobbyList.SetActive(!_showBoard);
        if (boardList != null && boardList.activeSelf != _showBoard)
            boardList.SetActive(_showBoard);

        Tab(lobbyTabFace, lobbyTabLabel, !_showBoard, "teacher.tabLobby");
        Tab(boardTabFace, boardTabLabel, _showBoard, "teacher.tabBoard");
    }

    static void Tab(Image face, TextMeshProUGUI label, bool on, string key)
    {
        if (face != null)
        {
            Color c = ThemeManager.Get(ThemeRole.Surface);
            c.a = on ? 1f : 0f;
            face.color = c;
        }

        if (label != null)
        {
            label.text = AppStrings.Get(key);
            label.color = ThemeManager.Get(on ? ThemeRole.TextPrimary : ThemeRole.TextMuted);
            label.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
        }
    }

    static string StateText(TeacherService.StartState start)
    {
        if (start == null)
            return AppStrings.Get("team.working");

        if (start.ended)
            return AppStrings.Get("teacher.state.ended");

        if (start.started)
        {
            string at = Clock(start.started_at);
            return at != null ? string.Format(AppStrings.Get("teacher.state.startedAt"), at)
                              : AppStrings.Get("teacher.state.started");
        }

        if (start.start_mode == "manual")
            return AppStrings.Get("teacher.state.manual");

        if (start.start_mode == "auto")
        {
            string at = Clock(start.starts_at);
            return at != null ? string.Format(AppStrings.Get("teacher.state.startsAt"), at)
                              : AppStrings.Get("teacher.state.manual");
        }

        return AppStrings.Get("teacher.state.open");
    }

    /// <summary>"14:05" in the phone's time, or null for no time.</summary>
    static string Clock(string iso)
    {
        if (string.IsNullOrEmpty(iso))
            return null;

        DateTime when;
        if (!DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                               System.Globalization.DateTimeStyles.AdjustToUniversal, out when))
            return null;

        return DateTime.SpecifyKind(when, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm");
    }

    void DrawLobby(TeacherService.Lobby lobby)
    {
        bool loaded = _teams != null;
        int used = 0;

        for (int i = 0; i < lobby.teams.Count; i++)
        {
            TeacherService.LobbyTeam team = lobby.teams[i];
            TeamCard card = Card(used++);
            if (card == null)
                break;
            FillCard(card, team.id, team.name, team.disqualified, team.players);
        }

        if (lobby.unteamed.Count > 0)
        {
            TeamCard card = Card(used++);
            if (card != null)
                FillCard(card, null, AppStrings.Get("teacher.noTeam"), false, lobby.unteamed);
        }

        for (int i = used; i < _cards.Count; i++)
            Hide(_cards[i].go);

        bool empty = used == 0;
        if (lobbyEmpty != null && lobbyEmpty.activeSelf != empty)
            lobbyEmpty.SetActive(empty);
        Set(lobbyEmptyTitle, AppStrings.Get(loaded ? "teacher.lobbyEmpty" : "team.working"));
        if (lobbyEmptyBody != null)
            lobbyEmptyBody.SetActive(loaded);
    }

    void FillCard(TeamCard card, string teamId, string name, bool disqualified,
                  List<TeacherService.LobbyPlayer> players)
    {
        card.teamId = teamId;
        card.teamName = string.IsNullOrEmpty(name) ? AppStrings.Get("board.hidden") : name;
        Set(card.name, card.teamName);

        int gps = 0;
        for (int i = 0; i < players.Count; i++)
            if (players[i].gps)
                gps++;

        if (card.meta != null)
        {
            if (disqualified)
                card.meta.text = AppStrings.Get("teacher.dqTag");
            else if (teamId == null)
                card.meta.text = string.Format(AppStrings.Get("teacher.people"), players.Count);
            else
                card.meta.text = string.Format(AppStrings.Get("teacher.teamMeta"), players.Count, gps);

            card.meta.color = ThemeManager.Get(disqualified ? ThemeRole.StatusPoor : ThemeRole.TextMuted);
        }

        if (card.disqualify != null)
        {
            bool can = !disqualified && !string.IsNullOrEmpty(teamId);
            if (card.disqualify.gameObject.activeSelf != can)
                card.disqualify.gameObject.SetActive(can);
            card.disqualify.interactable = !_acting;
        }

        string me = Me;
        int used = 0;
        for (int i = 0; i < players.Count; i++)
        {
            PlayerRow row = Row(card, used++);
            if (row == null)
                break;

            TeacherService.LobbyPlayer p = players[i];
            row.userId = p.userId;
            row.who = string.IsNullOrEmpty(p.name) ? AppStrings.Get("teacher.player") : p.name;

            bool mine = !string.IsNullOrEmpty(me) && p.userId == me;
            Set(row.name, mine ? row.who + "  " + AppStrings.Get("team.you") : row.who);

            if (row.dot != null)
                row.dot.color = ThemeManager.Get(p.gps ? ThemeRole.GoodDot : ThemeRole.FillStrong);

            bool act = !mine && !string.IsNullOrEmpty(p.userId);
            if (row.kick != null)
            {
                if (row.kick.gameObject.activeSelf != act) row.kick.gameObject.SetActive(act);
                row.kick.interactable = !_acting;
            }
            if (row.ban != null)
            {
                if (row.ban.gameObject.activeSelf != act) row.ban.gameObject.SetActive(act);
                row.ban.interactable = !_acting;
            }
        }

        for (int i = used; i < card.rows.Count; i++)
            Hide(card.rows[i].go);
    }

    void DrawBoard()
    {
        List<TeacherService.BoardEntry> sorted = TeacherService.BuildBoard(_teams, _disqualified);
        int used = 0;

        for (int i = 0; i < sorted.Count; i++)
        {
            BoardRow row = BoardLine(used++);
            if (row == null)
                break;

            TeacherService.BoardEntry s = sorted[i];
            Set(row.rank, s.rank.ToString());
            Set(row.name, string.IsNullOrEmpty(s.name) ? AppStrings.Get("board.hidden") : s.name);
            Set(row.meta, string.Format(AppStrings.Get(s.solved == 1 ? "teacher.stopsOne" : "teacher.stops"),
                                        s.solved));
            Set(row.points, string.Format(AppStrings.Get("teacher.points"), s.points));
        }

        for (int i = used; i < _boardRows.Count; i++)
            Hide(_boardRows[i].go);

        bool empty = used == 0;
        if (boardEmpty != null && boardEmpty.activeSelf != empty)
            boardEmpty.SetActive(empty);
    }

    // ------------------------------------------------------------------ pools

    TeamCard Card(int index)
    {
        while (_cards.Count <= index)
        {
            if (teamTemplate == null || lobbyContent == null)
                return null;

            GameObject go = Instantiate(teamTemplate, lobbyContent);
            go.name = "Team";
            TeamCard card = new TeamCard
            {
                go = go,
                name = Find<TextMeshProUGUI>(go.transform, "Head/Text/Name"),
                meta = Find<TextMeshProUGUI>(go.transform, "Head/Text/Meta"),
                disqualify = Find<Button>(go.transform, "Head/Disqualify"),
                members = Find<RectTransform>(go.transform, "Members")
            };

            if (card.disqualify != null)
            {
                card.disqualify.onClick.RemoveAllListeners();
                card.disqualify.onClick.AddListener(() => AskDisqualify(card));
            }

            _cards.Add(card);
        }

        TeamCard found = _cards[index];
        if (!found.go.activeSelf)
            found.go.SetActive(true);
        return found;
    }

    PlayerRow Row(TeamCard card, int index)
    {
        while (card.rows.Count <= index)
        {
            if (playerTemplate == null || card.members == null)
                return null;

            GameObject go = Instantiate(playerTemplate, card.members);
            go.name = "Player";
            PlayerRow row = new PlayerRow
            {
                go = go,
                dot = Find<Image>(go.transform, "Dot"),
                name = Find<TextMeshProUGUI>(go.transform, "Name"),
                kick = Find<Button>(go.transform, "Kick"),
                ban = Find<Button>(go.transform, "Ban")
            };

            if (row.kick != null)
            {
                row.kick.onClick.RemoveAllListeners();
                row.kick.onClick.AddListener(() => AskKick(row));
            }

            if (row.ban != null)
            {
                row.ban.onClick.RemoveAllListeners();
                row.ban.onClick.AddListener(() => AskBan(row));
            }

            card.rows.Add(row);
        }

        PlayerRow found = card.rows[index];
        if (!found.go.activeSelf)
            found.go.SetActive(true);
        return found;
    }

    BoardRow BoardLine(int index)
    {
        while (_boardRows.Count <= index)
        {
            if (boardTemplate == null || boardContent == null)
                return null;

            GameObject go = Instantiate(boardTemplate, boardContent);
            go.name = "Standing";
            _boardRows.Add(new BoardRow
            {
                go = go,
                rank = Find<TextMeshProUGUI>(go.transform, "Rank"),
                name = Find<TextMeshProUGUI>(go.transform, "Text/Name"),
                meta = Find<TextMeshProUGUI>(go.transform, "Text/Meta"),
                points = Find<TextMeshProUGUI>(go.transform, "Points")
            });
        }

        BoardRow found = _boardRows[index];
        if (!found.go.activeSelf)
            found.go.SetActive(true);
        return found;
    }

    static T Find<T>(Transform root, string path) where T : Component
    {
        Transform t = root.Find(path);
        return t != null ? t.GetComponent<T>() : null;
    }

    static void Set(TextMeshProUGUI label, string text)
    {
        if (label != null && label.text != text)
            label.text = text;
    }

    static void Hide(GameObject go)
    {
        if (go != null && go.activeSelf)
            go.SetActive(false);
    }

    // ---------------------------------------------------------------- preview

    /// <summary>
    /// Fills the panel with a made-up class, without the network, for the
    /// screenshot harness (UIPreview runs in edit mode, where nothing polls).
    /// </summary>
    public void PreviewSample(bool board)
    {
        _world = "preview";
        _name = AppStrings.CurrentLanguage == AppLanguage.Bulgarian ? "Старият Ямбол" : "Old Yambol";
        _showBoard = board;
        _start = new TeacherService.StartState { start_mode = "manual", started = false };

        _teams = new List<TeacherService.TeamRow>
        {
            new TeacherService.TeamRow
            {
                id = "t1", name = "Зелените", solved = 6, points = 180,
                members = new[]
                {
                    new TeacherService.TeamMember { user_id = "u1", name = "Мария", gps_ok = true },
                    new TeacherService.TeamMember { user_id = "u2", name = "Иван", gps_ok = true },
                    new TeacherService.TeamMember { user_id = "u3", name = "Петя", gps_ok = false }
                }
            },
            new TeacherService.TeamRow
            {
                id = "t2", name = "Scan Squad", solved = 5, points = 160, adjusted = -20,
                members = new[]
                {
                    new TeacherService.TeamMember { user_id = "u4", name = "Alex", gps_ok = true },
                    new TeacherService.TeamMember { user_id = "u5", name = "Николай", gps_ok = false }
                }
            },
            new TeacherService.TeamRow
            {
                id = "t3", name = "Бързите", disqualified_at = "2026-11-11T10:00:00Z",
                members = new[] { new TeacherService.TeamMember { user_id = "u6", name = "Георги", gps_ok = true } }
            }
        };

        _players = new List<TeacherService.Player>
        {
            new TeacherService.Player { user_id = "u7", name = "Елена", team_id = null, gps_ok = false }
        };

        _disqualified.Clear();
        _disqualified.Add("t3");

        if (panel != null)
            panel.SetActive(true);
        Draw();
    }
}
