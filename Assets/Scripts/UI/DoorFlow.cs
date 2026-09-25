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
    public const string WelcomeSeenKey = "welcome_seen";

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
        Team,

        /// <summary>
        /// Making an account, asked for from the team card when a scored trail
        /// needs one. "Not now" is always there: a guided walk does not need one,
        /// and a screen that will not let you past is how people give up on an app.
        /// </summary>
        Account,

        /// <summary>The end of the demo: find a trail near you, or use a code.</summary>
        Ready,

        /// <summary>Under 14 and no parent has agreed yet.</summary>
        Consent
    }

    [Header("Screens")]
    public GameObject welcomePanel;
    public GameObject termsPanel;
    public GameObject trailsPanel;
    public GameObject codePanel;

    [Tooltip("Who you are walking with, and the standings. Opened from the map " +
             "rather than by the flow itself - it is not a gate, it is a screen.")]
    public GameObject teamPanel;

    [Tooltip("The account form, opened from the team card.")]
    public GameObject accountPanel;

    [Tooltip("The card at the end of the demo.")]
    public GameObject readyPanel;

    [Tooltip("Asks an under-14 for a parent's email, then waits for the parent.")]
    public GameObject consentPanel;
    public ConsentDoorView consent;

    [Tooltip("The age question on the terms screen. Accept waits for an answer.")]
    public AgeChoiceView termsAge;

    [Tooltip("Runs the demo. On an object that stays active, so it keeps " +
             "listening while the camera tab is up.")]
    public DemoCoach coach;

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

    [Tooltip("The team card. Without this the Team door hides the interface " +
             "and shows nothing, because Open has nothing to reveal.")]
    public TeamDoorView team;

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

    /// <summary>Set by "Try it now", spent after the terms: the demo starts.</summary>
    bool _wantsDemo;

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
        Hide(accountPanel);
        Hide(readyPanel);
        Hide(consentPanel);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        TermsService.OnAcceptedChanged += Evaluate;
        ConsentService.OnChanged += Evaluate;
        TrailService.OnTrailsChanged += HandleTrailsChanged;
        SupabaseAuth.OnSessionChanged += Evaluate;
        SupabaseAuth.OnSessionChanged += AccountMaybeMade;
    }

    void OnDisable()
    {
        TermsService.OnAcceptedChanged -= Evaluate;
        ConsentService.OnChanged -= Evaluate;
        TrailService.OnTrailsChanged -= HandleTrailsChanged;
        SupabaseAuth.OnSessionChanged -= Evaluate;
        SupabaseAuth.OnSessionChanged -= AccountMaybeMade;
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
        // Somebody filling in the account form is not interrupted by the session
        // it is about to change. It closes itself when the account exists.
        if (Current == Door.Account)
            return;

        // A door the reader opened on purpose stays open until they close it.
        // Refreshing the trail list underneath must not throw them out of it.
        if (_reopened && Current != Door.None)
            return;

        TermsService terms = TermsService.Instance;

        // Whoever gets the session event first, the question goes out before
        // the answer is read.
        if (terms != null && !terms.Accepted)
            terms.AskServer();

        // Checking: signing into an account that accepted on another phone. The
        // server has the record, and asking again while it answers is the
        // "terms twice" people saw.
        if (terms != null && SupabaseAuth.HasSettledYet && !terms.Accepted && !terms.Checking)
        {
            Show(PlayerPrefs.GetInt(WelcomeSeenKey, 0) == 0 ? Door.Welcome : Door.Terms);
            return;
        }

        if (_wantsCode && !NeedsParent)
        {
            _wantsCode = false;
            Show(Door.Code);
            return;
        }

        if (_wantsDemo)
        {
            _wantsDemo = false;
            StartDemo();
            return;
        }

        // The demo and its closing card own the screen until they are done.
        if (DemoTrail.Active || (coach != null && coach.Running) || Current == Door.Ready)
        {
            if (Current != Door.Ready)
                Show(Door.None);
            return;
        }

        // Under 14, and no parent has said yes: the one door that leads anywhere
        // is the one that asks them. Everything past it would be refused.
        if (NeedsParent)
        {
            Show(Door.Consent);
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

    /// <summary>Welcome: "Try it now". The terms, then the demo.</summary>
    public void PassWelcome()
    {
        _wantsDemo = !_wantsCode;
        PlayerPrefs.SetInt(WelcomeSeenKey, 1);
        PlayerPrefs.Save();
        Show(Door.Terms);
    }

    /// <summary>Starts the demo, on a first run or from Settings.</summary>
    public void StartDemo()
    {
        _reopened = false;
        _dismissed = false;
        Show(Door.None);

        if (coach != null)
            coach.Begin();
    }

    /// <summary>The demo is over: where to go next.</summary>
    public void ShowReady()
    {
        _reopened = false;
        _dismissed = false;
        Show(Door.Ready);
    }

    /// <summary>Ready: "Find a trail near you".</summary>
    public void OpenLibrary()
    {
        _dismissed = false;
        Show(NeedsParent ? Door.Consent : Door.Trails);
    }

    static bool NeedsParent
    {
        get { return ConsentService.Instance != null && ConsentService.Instance.NeedsParent; }
    }

    /// <summary>
    /// Consent: "Change my answer". Back to the terms, where the age question
    /// is asked again. Self-declared either way; this only makes a slip fixable.
    /// </summary>
    public void ReaskAge()
    {
        _reopened = false;
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

    /// <summary>Terms: "I accept". Only with the age question answered.</summary>
    public void AcceptTerms()
    {
        bool? young = termsAge != null ? termsAge.Chosen : false;

        // 12 or younger, or nothing chosen: the terms stay unaccepted and the
        // question says why.
        if (!young.HasValue || (termsAge != null && termsAge.TooYoung))
        {
            termsAge.Nag();
            return;
        }

        if (TermsService.Instance != null)
        {
            TermsService.Instance.Accept(young.Value);

            // Accepting again with the same version does not raise a change, so
            // an answer changed from the consent door would sit there unheard.
            Evaluate();
        }
        else
        {
            Evaluate();   // no service in the scene: do not trap the reader here
        }
    }

    /// <summary>Trails: "Join with a code", and the empty state's button.</summary>
    public void OpenCode()
    {
        _dismissed = false;
        Show(NeedsParent ? Door.Consent : Door.Code);
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
        Show(NeedsParent ? Door.Consent : Door.Trails);
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

        // A real trail was chosen part way through the demo.
        if (DemoTrail.Active && coach != null)
            coach.Stop();

        Show(Door.None);

        // Ask the server about this trail, then gate on the answer.
        //
        // The gate used to read SelectedIsGame, which resolves by looking the
        // trail up in the loaded list - and joining by code reaches here before
        // that list has been refetched, so the trail was not in it, the mode was
        // unknown, and the gate quietly skipped. That is the path everybody
        // takes on the day. trail_summary answers about one trail by id and does
        // not wait for the list.
        TrailService trails = TrailService.Instance;

        if (trails != null)
            trails.LoadNeedsAccount(trails.SelectedId, () => StartCoroutine(TeamGate()));
        else
            StartCoroutine(TeamGate());
    }

    // ----------------------------------------------------------- account

    /// <summary>
    /// The account form, asked for from the team card. When it closes the team
    /// gate runs again, which is what brings the reader back to the team card -
    /// now with a form they are allowed to use.
    /// </summary>
    public void OpenAccount()
    {
        _dismissed = false;
        Show(Door.Account);
    }

    /// <summary>"Not now", or an account that now exists. Either way, on to teams.</summary>
    public void AccountDone()
    {
        if (Current != Door.Account)
            return;

        Show(Door.None);
        StartCoroutine(TeamGate());
    }

    void AccountMaybeMade()
    {
        if (Current != Door.Account)
            return;

        SupabaseAuth auth = SupabaseAuth.Instance;

        if (auth != null && auth.IsSignedIn && !auth.IsAnonymous)
            AccountDone();
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

        if (trails == null)
            yield break;

        // The server's answer where there is one, the list only as a fallback.
        string mode = trails.SelectedSummaryMode;

        bool game = !string.IsNullOrEmpty(mode)
            ? TrailService.IsGame(mode)
            : trails.SelectedIsGame;

        if (!game)
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
        Hide(accountPanel);
        Hide(readyPanel);
        Hide(consentPanel);

        // The app itself is up, which is the moment to ask where it is - not
        // cold over the welcome screen.
        if (door == Door.None && UserLocationService.Instance != null)
            UserLocationService.Instance.Begin();

        // The two card doors are the ones the map shows through. Welcome and Terms
        // are plain: nothing behind them, because there is nothing yet to look at.
        bool wantsMap = door == Door.Trails || door == Door.Code || door == Door.Team ||
                        door == Door.Account || door == Door.Ready || door == Door.Consent;

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

            // Missing until a phone found it. Every other door had a case here
            // and this one did not, so asking for a team turned the app off:
            // the navigation bar and the map's own controls are switched off by
            // Show for any door but None, the blur goes up, and then nothing
            // was put in front of it. A walker joining a game from the trails
            // list got a blurred map, no controls, and no way out.
            case Door.Team:
                Reveal(teamPanel);
                if (team != null)
                    team.Refresh();
                break;

            case Door.Account:
                Reveal(accountPanel);
                break;

            case Door.Ready:
                Reveal(readyPanel);
                break;

            case Door.Consent:
                Reveal(consentPanel);
                if (consent != null)
                    consent.Refresh();
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
