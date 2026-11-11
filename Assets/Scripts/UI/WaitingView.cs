using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// "Чакаме учителя да започне": what a student sees on a trail whose start is
/// the organiser's (start_mode 'manual') until the organiser presses Start.
///
/// It decides for itself when to be up, from what the app already knows:
/// TrailService.SelectedStart (trail_start, 0058, asked when a trail is entered
/// and every 20 s while waiting), the trail's role (the owner and editors never
/// wait), and DoorFlow (a door - the team card, the trail list - goes over it,
/// and it comes back when the door closes). While it is up it asks trail_start
/// itself every five seconds, and the team every ten; nothing runs while it is
/// down. When the start comes it closes, tells TrailService (which fetches the
/// stops at once) and the walk begins.
///
/// "To the map" puts it away for this trail until the app is opened again; the
/// next-stop card on the map still says the game waits for its start.
///
/// Sits on an always-active holder so it can listen; the screen is a child.
/// </summary>
public class WaitingView : MonoBehaviour
{
    public const int MaxMembers = 12;

    public float pollSeconds = 5f;

    public GameObject panel;
    public TextMeshProUGUI teamLabel;

    [Tooltip("Shown instead of the names when the student is on no team yet.")]
    public GameObject noTeamNote;

    public GameObject[] memberRows = new GameObject[MaxMembers];
    public TextMeshProUGUI[] memberNames = new TextMeshProUGUI[MaxMembers];
    public Image[] memberDots = new Image[MaxMembers];

    [Tooltip("Opens the team card: make or join a team, or see the code.")]
    public Button teamButton;
    public TextMeshProUGUI teamButtonLabel;

    [Tooltip("Puts the screen away for this trail. A WayOut, so back does the same.")]
    public Button laterButton;

    public static WaitingView Instance { get; private set; }

    /// <summary>True while the waiting screen is on screen.</summary>
    public static bool IsShowing
    {
        get { return Instance != null && Instance.panel != null && Instance.panel.activeSelf; }
    }

    string _laterFor;
    string _world;
    Coroutine _poll;

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
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        TrailService.OnTrailsChanged += Evaluate;
        TeamService.OnTeamChanged += Draw;
        DoorFlow.OnDoorChanged += HandleDoor;
        TeacherPanelView.OnOpenChanged += Evaluate;
        SupabaseAuth.OnSessionChanged += Evaluate;
        AppStrings.OnLanguageChanged += Draw;
        ThemeManager.OnThemeChanged += Draw;

        if (teamButton != null)
        {
            teamButton.onClick.RemoveAllListeners();
            teamButton.onClick.AddListener(OpenTeam);
        }

        if (laterButton != null)
        {
            laterButton.onClick.RemoveAllListeners();
            laterButton.onClick.AddListener(Later);
        }

        Evaluate();
    }

    void OnDisable()
    {
        TrailService.OnTrailsChanged -= Evaluate;
        TeamService.OnTeamChanged -= Draw;
        DoorFlow.OnDoorChanged -= HandleDoor;
        TeacherPanelView.OnOpenChanged -= Evaluate;
        SupabaseAuth.OnSessionChanged -= Evaluate;
        AppStrings.OnLanguageChanged -= Draw;
        ThemeManager.OnThemeChanged -= Draw;
        StopPolling();
    }

    void HandleDoor(DoorFlow.Door door)
    {
        Evaluate();
    }

    // -------------------------------------------------------------- deciding

    /// <summary>
    /// Whether this phone should be waiting now. Public so the walk-start path
    /// can ask again at once (DoorFlow.Entered and the team gate call it via the
    /// events this already listens to, so nobody has to).
    /// </summary>
    public static bool ShouldWait(out string worldId)
    {
        worldId = null;

        TrailService trails = TrailService.Instance;
        if (trails == null || string.IsNullOrEmpty(trails.SelectedId))
            return false;

        worldId = trails.SelectedId;

        TrailService.StartInfo start = trails.SelectedStart;
        if (start == null || start.start_mode != "manual" || start.started || start.ended)
            return false;

        // Whoever builds or runs the trail never waits for themselves.
        TrailService.Trail trail = trails.Selected;
        if (trail != null && (trail.role == "owner" || trail.role == "editor"))
            return false;

        if (DemoTrail.Active)
            return false;

        return true;
    }

    public void Evaluate()
    {
        string world;
        bool wait = ShouldWait(out world)
                    && world != _laterFor
                    && (DoorFlow.Instance == null || DoorFlow.Instance.Current == DoorFlow.Door.None)
                    && !TeacherPanelView.IsOpen;

        if (wait)
            Show(world);
        else
            Hide();
    }

    void Show(string world)
    {
        if (panel == null)
            return;

        bool was = panel.activeSelf;
        _world = world;

        if (!was)
        {
            panel.SetActive(true);

            TeamService teams = TeamService.Instance;
            if (teams != null && !teams.Busy)
                teams.Load(world);
        }

        Draw();

        if (_poll == null)
            _poll = StartCoroutine(Poll());
    }

    void Hide()
    {
        StopPolling();

        if (panel != null && panel.activeSelf)
            panel.SetActive(false);
    }

    void StopPolling()
    {
        if (_poll != null)
        {
            StopCoroutine(_poll);
            _poll = null;
        }
    }

    // --------------------------------------------------------------- polling

    IEnumerator Poll()
    {
        int tick = 0;

        while (panel != null && panel.activeSelf)
        {
            yield return new WaitForSecondsRealtime(pollSeconds);

            string world = _world;
            TeacherService.StartState start = null;
            yield return TeacherService.FetchStart(world, (s, e) => { start = s; });

            if (world != _world || panel == null || !panel.activeSelf)
                break;

            if (start != null && (start.started || start.ended || start.start_mode != "manual"))
            {
                Started(world, start.started);
                break;
            }

            // The team every other time: teammates arriving, GPS dots turning green.
            TeamService teams = TeamService.Instance;
            if (++tick % 2 == 0 && teams != null && !teams.Busy && !teams.Writing)
                teams.Load(world);
        }

        _poll = null;
    }

    void Started(string world, bool started)
    {
        _poll = null;
        Hide();

        if (started)
            ProximityToast.Say(AppStrings.Get("wait.started"));

        // TrailService asks trail_start again, sees the whistle, fetches the
        // stops at once and tells everybody (0058's own path).
        TrailService trails = TrailService.Instance;
        if (trails != null && trails.SelectedId == world)
            trails.LoadNeedsAccount(world);
    }

    // --------------------------------------------------------------- buttons

    void OpenTeam()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenTeam();
    }

    /// <summary>"To the map": away for this trail, until the app opens again.</summary>
    public void Later()
    {
        _laterFor = _world;
        Hide();
    }

    // ---------------------------------------------------------------- drawing

    void Draw()
    {
        if (panel == null || !panel.activeSelf)
            return;

        TeamService teams = TeamService.Instance;
        TeamService.Detail team = teams != null && teams.World == _world ? teams.Team : null;
        bool onTeam = team != null && team.on_team;

        if (teamLabel != null)
            teamLabel.text = onTeam ? string.Format(AppStrings.Get("wait.team"), team.name)
                                    : AppStrings.Get("wait.noTeam");

        if (noTeamNote != null && noTeamNote.activeSelf == onTeam)
            noTeamNote.SetActive(!onTeam);

        int count = onTeam && team.members != null ? team.members.Length : 0;

        for (int i = 0; i < memberRows.Length; i++)
        {
            bool used = i < count && team.members[i] != null;
            if (memberRows[i] != null && memberRows[i].activeSelf != used)
                memberRows[i].SetActive(used);
            if (!used)
                continue;

            TeamService.Member member = team.members[i];

            if (i < memberNames.Length && memberNames[i] != null)
            {
                string line = member.is_me ? member.name + "  " + AppStrings.Get("team.you") : member.name;
                if (member.is_captain)
                    line += "  ★";
                memberNames[i].text = line;
            }

            if (i < memberDots.Length && memberDots[i] != null)
                memberDots[i].color = ThemeManager.Get(member.gps ? ThemeRole.GoodDot : ThemeRole.FillStrong);
        }

        if (teamButtonLabel != null)
            teamButtonLabel.text = AppStrings.Get(onTeam ? "wait.openTeam" : "wait.makeTeam");
    }

    // ---------------------------------------------------------------- preview

    /// <summary>A made-up team, without the network, for the screenshot harness.</summary>
    public void PreviewSample()
    {
        if (panel == null)
            return;

        panel.SetActive(true);

        if (teamLabel != null)
            teamLabel.text = string.Format(AppStrings.Get("wait.team"), "Зелените");
        if (noTeamNote != null)
            noTeamNote.SetActive(false);

        string[] names = { "Мария  " + AppStrings.Get("team.you") + "  ★", "Иван", "Петя" };
        bool[] gps = { true, true, false };

        for (int i = 0; i < memberRows.Length; i++)
        {
            bool used = i < names.Length;
            if (memberRows[i] != null)
                memberRows[i].SetActive(used);
            if (!used)
                continue;
            if (i < memberNames.Length && memberNames[i] != null)
                memberNames[i].text = names[i];
            if (i < memberDots.Length && memberDots[i] != null)
                memberDots[i].color = ThemeManager.Get(gps[i] ? ThemeRole.GoodDot : ThemeRole.FillStrong);
        }

        if (teamButtonLabel != null)
            teamButtonLabel.text = AppStrings.Get("wait.openTeam");
    }
}
