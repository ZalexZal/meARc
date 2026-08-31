using System;
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
        Code
    }

    [Header("Screens")]
    public GameObject welcomePanel;
    public GameObject termsPanel;
    public GameObject trailsPanel;
    public GameObject codePanel;

    [Header("Behind them")]
    [Tooltip("The blurred still of the map. Only the card doors use it - Welcome " +
             "and Terms are plain, because they carry nothing but words.")]
    public FrostedBackdrop backdrop;

    [Tooltip("Hidden while a door is up. A modal with a live tab bar behind it is a " +
             "tab bar people tap straight past.")]
    public GameObject navBar;

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

    void Awake()
    {
        Instance = this;
        Hide(welcomePanel);
        Hide(termsPanel);
        Hide(trailsPanel);
        Hide(codePanel);
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
        Show(Door.Code);
    }

    /// <summary>Code: "Cancel".</summary>
    public void CloseCode()
    {
        Show(Door.Trails);
    }

    /// <summary>The trail name on the map. A door the reader chose to open.</summary>
    public void OpenTrails()
    {
        _reopened = true;
        Show(Door.Trails);
    }

    /// <summary>
    /// Leaves whichever door is up, if there is anything to go back to. A reader
    /// who has not chosen a trail has nothing behind this screen, so the control
    /// that calls this is hidden in that case rather than doing nothing.
    /// </summary>
    public void Close()
    {
        _reopened = false;

        TrailService service = TrailService.Instance;
        if (service == null || string.IsNullOrEmpty(service.SelectedId))
        {
            Evaluate();
            return;
        }

        Show(Door.None);
    }

    /// <summary>A trail was chosen, from the list or by redeeming a code.</summary>
    public void Entered()
    {
        _reopened = false;
        Show(Door.None);
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

        // The two card doors are the ones the map shows through. Welcome and Terms
        // are plain: nothing behind them, because there is nothing yet to look at.
        bool wantsMap = door == Door.Trails || door == Door.Code;

        if (navBar != null)
            navBar.SetActive(door == Door.None);

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
                {
                    trails.SetCanClose(CanClose);
                    trails.Refresh();
                }
                break;

            case Door.Code:
                Reveal(codePanel);
                if (code != null)
                    code.Begin();
                break;
        }
    }

    /// <summary>Whether there is an app behind the door to go back to.</summary>
    bool CanClose
    {
        get
        {
            TrailService service = TrailService.Instance;
            return service != null && !string.IsNullOrEmpty(service.SelectedId);
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
