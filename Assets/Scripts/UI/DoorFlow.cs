using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Which way-in screen is up, and why.
///
/// Four doors, in a fixed order, because each one depends on the last: you cannot
/// agree to terms you have not been shown, you cannot join a trail before there is
/// an identity to join it with, and you cannot walk a trail you have not chosen.
///
/// Everything here is decided from state, not from a sequence of button presses.
/// Anything that changes the state - accepting the terms, joining a trail, signing
/// in - just calls <see cref="Evaluate"/> again and the right door opens. That is
/// what makes the reopened case work: tapping the trail name on the map opens the
/// same screen the launch flow opens, rather than a second one that has to be kept
/// in step with it.
/// </summary>
public class DoorFlow : MonoBehaviour
{
    const string WelcomeSeenKey = "welcome_seen";

    public enum Door
    {
        /// <summary>No door. The app itself.</summary>
        None,
        Welcome,
        Terms,
        Trails,
        Code,

        /// <summary>
        /// Who you are walking with. Not a gate: nothing waits behind it and the
        /// reader opens it because they want to, which is why nothing in
        /// Evaluate ever shows it.
        /// </summary>
        Team
    }

    [Header("Screens")]
    public GameObject welcomePanel;
    public GameObject termsPanel;
    public GameObject trailsPanel;
    public GameObject codePanel;

    [Tooltip("Who you are walking with, and the standings. Opened from the map " +
             "rather than by the flow itself - it is not a gate, it is a screen.")]
    public GameObject teamPanel;

    [Header("Behind them")]
    [Tooltip("The blurred still of the map. Only the card doors use it - Welcome " +
             "and Terms are plain, because they carry nothing but words.")]
    public FrostedBackdrop backdrop;

    [Tooltip("Hidden while a door is up. A modal with a live tab bar behind it is a " +
             "tab bar people tap straight past.")]
    public GameObject navBar;

    [Tooltip("The map's own controls - its top bar, scale, attribution and dock. " +
             "Hidden before the shutter, so what the blur catches is a map rather " +
             "than a screenshot of the app.")]
    public GameObject[] hideWhileOpen = new GameObject[0];

    [Header("Screens that fill themselves in")]
    public TrailsDoor trails;
    public CodeDoor code;

    /// <summary>Raised whenever the door changes, so the app can pause what it does.</summary>
    public static event Action<Door> OnDoorChanged;

    public static DoorFlow Instance { get; private set; }

    public Door Current { get; private set; } = Door.None;

    /// <summary>
    /// True when the reader opened a door themselves rather than being shown it.
    /// It is what makes Cancel possible: a launch-flow door has nothing to go back
    /// to, and a reopened one does.
    /// </summary>
    bool _reopened;

    /// <summary>Set by "I have a code" on the welcome screen, spent after the terms.</summary>
    bool _wantsCode;

    /// <summary>
    /// The reader has said "not now" to picking a trail, and is to be left alone
    /// until they ask again.
    ///
    /// Without this the app soft-locks, and it did: Evaluate opened the trail list
    /// whenever no trail was selected, Close called Evaluate, and the only way to
    /// select a trail was a join code the server cannot yet redeem. Two screens,
    /// each of which only led to the other, and a hidden nav bar behind both.
    /// </summary>
    bool _dismissed;

    void Awake()
    {
        Instance = this;
        Hide(welcomePanel);
        Hide(termsPanel);
        Hide(trailsPanel);
        Hide(codePanel);
        Hide(teamPanel);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        TermsService.OnAcceptedChanged += Evaluate;
        TrailService.OnTrailsChanged += HandleTrailsChanged;
        SupabaseAuth.OnSessionChanged += Evaluate;
    }

    void OnDisable()
    {
        TermsService.OnAcceptedChanged -= Evaluate;
        TrailService.OnTrailsChanged -= HandleTrailsChanged;
        SupabaseAuth.OnSessionChanged -= Evaluate;
    }

    void Start()
    {
        Evaluate();
    }

    // ------------------------------------------------------------- the decision

    /// <summary>
    /// Opens whichever door the current state calls for. Safe to call at any time
    /// and as often as you like; it settles on the same answer.
    /// </summary>
    public void Evaluate()
    {
        // A door the reader opened on purpose stays open until they close it.
        // Refreshing the trail list underneath must not throw them out of it.
        if (_reopened && Current != Door.None)
            return;

        TermsService terms = TermsService.Instance;

        if (terms != null && !terms.Accepted)
        {
            Show(PlayerPrefs.GetInt(WelcomeSeenKey, 0) == 0 ? Door.Welcome : Door.Terms);
            return;
        }

        if (_wantsCode)
        {
            _wantsCode = false;
            Show(Door.Code);
            return;
        }

        // Asked once, then left alone. The map is worth having without a trail - it
        // still shows the reader where they are - and the trail chip at the top of
        // it is how they come back. Re-offering the list on every membership
        // refresh is how this became a screen with no way out.
        if (_dismissed)
        {
            Show(Door.None);
            return;
        }

        TrailService service = TrailService.Instance;
        bool hasTrail = service != null && !string.IsNullOrEmpty(service.SelectedId);

        // Not "no trails, so show the list": a reader who has not been answered yet
        // has no trails either, and slamming an empty list in their face while the
        // request is still in flight is how an app looks broken on a slow bar of
        // signal. The list itself shows the waiting state.
        Show(hasTrail ? Door.None : Door.Trails);
    }

    void HandleTrailsChanged()
    {
        if (trails != null)
            trails.Refresh();

        Evaluate();
    }

    // -------------------------------------------------------------- the buttons

    /// <summary>Welcome: "Get started".</summary>
    public void PassWelcome()
    {
        PlayerPrefs.SetInt(WelcomeSeenKey, 1);
        PlayerPrefs.Save();
        Show(Door.Terms);
    }

    /// <summary>
    /// Welcome: "I have a code". Still goes through the terms - a reader with a
    /// code has not agreed to anything yet - and lands on the code screen after.
    /// </summary>
    public void PassWelcomeWithCode()
    {
        _wantsCode = true;
        PassWelcome();
    }

    /// <summary>Terms: "I accept".</summary>
    public void AcceptTerms()
    {
        if (TermsService.Instance != null)
            TermsService.Instance.Accept();
        else
            Evaluate();   // no service in the scene: do not trap the reader here
    }

    /// <summary>Trails: "Join with a code", and the empty state's button.</summary>
    public void OpenCode()
    {
        _dismissed = false;
        Show(Door.Code);
    }

    /// <summary>Code: "Cancel".</summary>
    public void CloseCode()
    {
        Show(Door.Trails);
    }

    /// <summary>The standings, from the map. Always the reader's own choice.</summary>
    public void OpenTeam()
    {
        _reopened = true;
        _dismissed = false;
        Show(Door.Team);
    }

    /// <summary>The trail name on the map. A door the reader chose to open.</summary>
    public void OpenTrails()
    {
        _reopened = true;
        _dismissed = false;
        Show(Door.Trails);
    }

    /// <summary>
    /// Leaves whichever door is up.
    ///
    /// Always allowed, and it used to depend on already having a trail - which is
    /// the one state in which the reader most needs a way out, because it is the
    /// state they cannot leave by choosing something. There is always a map behind
    /// this, so there is always somewhere to go.
    /// </summary>
    public void Close()
    {
        _reopened = false;
        _dismissed = true;
        Show(Door.None);
    }

    /// <summary>A trail was chosen, from the list or by redeeming a code.</summary>
    public void Entered()
    {
        _reopened = false;
        _dismissed = false;
        Show(Door.None);

        // Whether this trail takes anonymous walkers at all, asked once on the
        // way in so the team card can say so before somebody types a team name
        // and is refused by the server.
        TrailService trails = TrailService.Instance;

        if (trails != null)
            trails.LoadNeedsAccount(trails.SelectedId);

        StartCoroutine(TeamGate());
    }

    /// <summary>
    /// A game is played in a team, so joining one asks for a team before it asks
    /// for anything else.
    ///
    /// Not politeness: since 0029 the server refuses to score a game for
    /// somebody with no team, so without this the first question a walker
    /// answered would come back "make or join a team first" with no obvious way
    /// to do either. A walk never sees this - it has no teams at all.
    ///
    /// The team is loaded before deciding. Asking somebody who is already on a
    /// team to make one is the kind of thing that makes an app feel like it has
    /// not been paying attention.
    /// </summary>
    IEnumerator TeamGate()
    {
        TrailService trails = TrailService.Instance;

        if (trails == null || !trails.SelectedIsGame)
            yield break;

        TeamService teams = TeamService.Instance;

        if (teams == null)
            yield break;

        teams.Load(trails.SelectedId);

        // Bounded. A server that never answers should not leave somebody staring
        // at a map they are not allowed to score on with no explanation, but it
        // should not hold the screen for ever either.
        float waited = 0f;

        while (teams.Busy && waited < 8f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (teams.Team == null || !teams.Team.on_team)
            Show(Door.Team);
    }

    // ------------------------------------------------------------------ showing

    void Show(Door door)
    {
        if (Current == door)
            return;

        Current = door;

        Hide(welcomePanel);
        Hide(termsPanel);
        Hide(trailsPanel);
        Hide(codePanel);
        Hide(teamPanel);

        // The two card doors are the ones the map shows through. Welcome and Terms
        // are plain: nothing behind them, because there is nothing yet to look at.
        bool wantsMap = door == Door.Trails || door == Door.Code || door == Door.Team;

        if (navBar != null)
            navBar.SetActive(door == Door.None);

        // Before the shutter, not after. The map's own bar, scale, attribution and
        // dock are the difference between a blurred map - which is what a reader
        // should see behind a card - and a blurred screenshot of the app, in which
        // every control is still legible enough to look pressable and none of them
        // are. That was most of what made these two screens look broken.
        for (int i = 0; i < hideWhileOpen.Length; i++)
        {
            if (hideWhileOpen[i] != null)
                hideWhileOpen[i].SetActive(door == Door.None);
        }

        if (OnDoorChanged != null)
            OnDoorChanged(door);

        if (backdrop == null)
        {
            Open(door);
            return;
        }

        backdrop.gameObject.SetActive(wantsMap);

        if (!wantsMap)
        {
            backdrop.Clear();
            Open(door);
            return;
        }

        // The card waits for the photograph. Everything above has already happened
        // - the nav bar is down, the old door is closed - so what the blur catches
        // is the map, which is the point of it.
        Door opening = door;
        backdrop.Capture(() =>
        {
            // A second door may have been asked for while the shutter was open.
            if (Current == opening)
                Open(opening);
        });
    }

    void Open(Door door)
    {
        switch (door)
        {
            case Door.Welcome:
                Reveal(welcomePanel);
                break;

            case Door.Terms:
                Reveal(termsPanel);
                break;

            case Door.Trails:
                Reveal(trailsPanel);
                if (trails != null)
                    trails.Refresh();
                break;

            case Door.Code:
                Reveal(codePanel);
                if (code != null)
                    code.Begin();
                break;
        }
    }

    /// <summary>
    /// Enabling is the whole of it: PanelFade runs its own fade from OnEnable, so
    /// there is nothing to start here.
    /// </summary>
    static void Reveal(GameObject go)
    {
        if (go != null)
            go.SetActive(true);
    }

    static void Hide(GameObject go)
    {
        if (go != null)
            go.SetActive(false);
    }
}
