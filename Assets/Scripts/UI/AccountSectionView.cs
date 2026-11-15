using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Signing up, signing in and signing out.
///
/// Sign-in and sign-up are two screens, not one form with two buttons. The
/// merged form asked somebody who already had an account to scroll past a name,
/// a role and four lines of work to find "Sign in", and asked somebody new to
/// guess which of two buttons made an account. Each screen now asks only what
/// it needs, and links to the other ("No account? Sign up" / "Have an account?
/// Sign in"). Which one opens is the caller's choice: <see cref="OpenDoor"/>.
///
/// Signing up converts the anonymous user in place rather than making a second
/// one. That is <see cref="SupabaseAuth.SignUp"/>'s job; what this class owes it
/// is never to offer the button while a call is already running, because two
/// PUTs racing on one session is a good way to end up signed in as nobody.
/// </summary>
public class AccountSectionView : MonoBehaviour
{
    /// <summary>The two screens somebody with no account can be shown.</summary>
    public enum AuthScreen { SignIn, SignUp }

    [Header("Anonymous")]
    [Tooltip("Everything shown to somebody with no account: both screens.")]
    public GameObject anonymousGroup;

    [Tooltip("The screen this view opens on when nobody asked for one.")]
    public AuthScreen startScreen = AuthScreen.SignUp;

    [Header("Sign up")]
    [Tooltip("The whole sign-up screen: the stage and the form under it.")]
    public GameObject signUpGroup;

    [Tooltip("What the leaderboard calls this person. Without it ensure_team " +
             "falls back to \"Player\" and four characters of their id, and a " +
             "board of six walkers is six rows nobody can tell apart.")]
    public TMP_InputField nameField;

    public TMP_InputField emailField;
    public TMP_InputField passwordField;

    [Tooltip("The A5 stage over the form: sky, scene and headline. Follows the answer.")]
    public RoleStage stage;

    [Tooltip("Creator or viewer. Required to make an account, with nothing chosen " +
             "to begin with.")]
    public RoleChoiceView roleChoice;

    [Tooltip("\"Choose how you will use meARc.\", right under the two cards.")]
    public TextMeshProUGUI roleError;

    [Tooltip("Broker, teacher, guide or other. Required of a creator only, and on " +
             "screen only once creator is chosen.")]
    public WorkChoiceView workChoice;

    [Tooltip("\"Choose your line of work.\", right under the choices, so it is " +
             "read where the answer is missing rather than at the foot of the card.")]
    public TextMeshProUGUI workError;

    public Button signUpButton;

    [Tooltip("\"Send the link again\". On screen only while a confirmation link is out.")]
    public Button resendButton;

    [Tooltip("\"Have an account? Sign in\": over to the sign-in screen.")]
    public Button toSignInButton;

    [Tooltip("Says what went wrong on the sign-up screen, or that something is " +
             "happening. Also the signed-in half's line. Empty when idle.")]
    public TextMeshProUGUI statusLabel;

    [Tooltip("The sign-up screen's way out (\"Not now\" / \"Continue as a guest\"). " +
             "Its words are written by DoorFlow; the sign-in screen's copies them.")]
    public TextMeshProUGUI laterLabel;

    [Header("Sign in")]
    [Tooltip("The whole sign-in screen.")]
    public GameObject signInGroup;

    public TMP_InputField signInEmailField;
    public TMP_InputField signInPasswordField;
    public Button signInButton;

    [Tooltip("\"No account? Sign up\": over to the sign-up screen.")]
    public Button toSignUpButton;

    [Tooltip("The sign-in screen's own status line. Falls back to statusLabel.")]
    public TextMeshProUGUI signInStatusLabel;

    [Tooltip("The sign-in screen's way out, worded as laterLabel is.")]
    public TextMeshProUGUI signInLaterLabel;

    [Header("No door")]
    [Tooltip("Where there is no room for either screen (the Profile page), two " +
             "buttons that open the account door on one of them.")]
    public Button openSignUpButton;
    public Button openSignInButton;

    [Header("Signed in")]
    public GameObject signedInGroup;
    public TextMeshProUGUI emailLabel;
    public TMP_InputField signedInNameField;
    public Button saveNameButton;
    public Button signOutButton;

    /// <summary>
    /// The screen the next account view to come on screen opens on, then
    /// forgotten. Null opens on that view's <see cref="startScreen"/>.
    /// </summary>
    public static AuthScreen? Next;

    static event System.Action NextRequested;

    /// <summary>
    /// Opens the account door on one of its two screens: "Create a profile"
    /// opens sign-up, "Sign in" opens sign-in. Safe with no door in the scene.
    /// </summary>
    public static void OpenDoor(AuthScreen screen)
    {
        Next = screen;

        // A view already on screen takes it now; one that is not takes it in
        // OnEnable, which the door's opening is about to cause.
        if (NextRequested != null)
            NextRequested();

        if (DoorFlow.Instance != null)
            DoorFlow.Instance.OpenAccount();

        // Used by now, synchronously; a door that never opened leaves no stale screen behind.
        Next = null;
    }

    public static void OpenSignIn() { OpenDoor(AuthScreen.SignIn); }
    public static void OpenSignUp() { OpenDoor(AuthScreen.SignUp); }

    AuthScreen _screen = AuthScreen.SignUp;

    /// <summary>Which of the two screens is up (or would be, with no account).</summary>
    public AuthScreen Current => _screen;


    bool _busy;

    void OnEnable()
    {
        Listen(signUpButton, SignUp);
        Listen(signInButton, SignIn);
        Listen(resendButton, Resend);
        Listen(toSignInButton, ShowSignIn);
        Listen(toSignUpButton, ShowSignUp);
        Listen(openSignUpButton, OpenSignUp);
        Listen(openSignInButton, OpenSignIn);
        Listen(signOutButton, SignOut);
        Listen(saveNameButton, SaveName);

        SupabaseAuth.OnSessionChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;
        NextRequested += TakeNext;

        if (workChoice != null)
            workChoice.OnChosen += WorkChosen;

        if (roleChoice != null)
            roleChoice.OnChosen += RoleChosen;

        // The screen somebody asked for; else the sign-up screen while a
        // confirmation link is out, because "I've opened the link" lives there;
        // else this view's own default.
        if (HasScreens)
        {
            _screen = Next ?? (Pending ? AuthScreen.SignUp : startScreen);
            Next = null;
        }

        ShowBanner();

        // On the way in as well as on the event: the account views live on
        // panels that are switched off for most of the app's life, so they are
        // reliably absent when the session actually changes.
        Refresh();
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
        NextRequested -= TakeNext;

        if (workChoice != null)
            workChoice.OnChosen -= WorkChosen;

        if (roleChoice != null)
            roleChoice.OnChosen -= RoleChosen;
    }

    static void Listen(Button button, UnityEngine.Events.UnityAction call)
    {
        if (button == null)
            return;

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(call);
    }

    bool HasScreens => signInGroup != null || signUpGroup != null;

    static bool Named
    {
        get
        {
            SupabaseAuth auth = SupabaseAuth.Instance;
            return auth != null && auth.IsSignedIn && !auth.IsAnonymous;
        }
    }

    // ------------------------------------------------------------ the screens

    public void ShowSignIn()
    {
        Show(AuthScreen.SignIn);
    }

    public void ShowSignUp()
    {
        Show(AuthScreen.SignUp);
    }

    /// <summary>Puts one of the two screens up, carrying the email across.</summary>
    public void Show(AuthScreen screen)
    {
        if (!HasScreens)
            return;

        if (screen != _screen)
            CarryEmail(screen);

        _screen = screen;

        // A message about the other screen is not about this one.
        if (!_busy)
            Say(null);

        Refresh();
    }

    void TakeNext()
    {
        if (!Next.HasValue || !HasScreens)
            return;

        AuthScreen screen = Next.Value;
        Next = null;
        Show(screen);
    }

    /// <summary>
    /// The email typed on one screen, on the other. Somebody told "there is
    /// already an account with that email" taps over to sign in, and should not
    /// have to type it twice.
    /// </summary>
    void CarryEmail(AuthScreen to)
    {
        TMP_InputField from = to == AuthScreen.SignIn ? emailField : signInEmailField;
        TMP_InputField into = to == AuthScreen.SignIn ? signInEmailField : emailField;

        if (from != null && into != null && from != into &&
            string.IsNullOrWhiteSpace(into.text) && !string.IsNullOrWhiteSpace(from.text))
            into.text = from.text.Trim();
    }

    // ------------------------------------------------------- role and work

    void WorkChosen()
    {
        HideWorkError();
        ShowBanner();
    }

    void RoleChosen()
    {
        HideRoleError();
        HideWorkError();
        ShowBanner();
    }

    void ShowBanner()
    {
        if (stage != null)
            stage.Show(roleChoice != null ? roleChoice.Selected : null,
                       workChoice != null ? workChoice.Selected : null);
    }

    void HideWorkError()
    {
        if (workError != null)
            workError.gameObject.SetActive(false);
    }

    void HideRoleError()
    {
        if (roleError != null)
            roleError.gameObject.SetActive(false);
    }

    /// <summary>A missing answer, said right under the question it belongs to.</summary>
    void SayUnder(TextMeshProUGUI under, string key)
    {
        if (under != null)
        {
            Say(null);
            under.text = AppStrings.Get(key);
            under.gameObject.SetActive(true);
        }
        else
        {
            SayPoor(key);
        }
    }

    // ------------------------------------------------------------- refresh

    /// <summary>Shows whichever half matches the session, and the screen asked for.</summary>
    public void Refresh()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;
        bool named = Named;

        if (anonymousGroup != null)
            anonymousGroup.SetActive(!named);

        if (signInGroup != null)
            signInGroup.SetActive(!named && _screen == AuthScreen.SignIn);

        if (signUpGroup != null)
            signUpGroup.SetActive(!named && _screen == AuthScreen.SignUp);

        if (signedInGroup != null)
            signedInGroup.SetActive(named);

        if (emailLabel != null)
        {
            emailLabel.text = auth != null && !string.IsNullOrWhiteSpace(auth.Email)
                ? auth.Email
                : "—";
        }

        // Only when it is not being typed into, or a repaint mid-edit would eat
        // what somebody was halfway through writing.
        if (signedInNameField != null && !signedInNameField.isFocused)
        {
            signedInNameField.text = auth != null && auth.DisplayName != null
                ? auth.DisplayName
                : string.Empty;
        }

        // DoorFlow words one way out ("Not now" or "Continue as a guest"); the
        // sign-in screen's says the same.
        if (signInLaterLabel != null && laterLabel != null)
            signInLaterLabel.text = laterLabel.text;

        SetBusy(_busy);
        DrawPending();
    }

    /// <summary>
    /// Waiting on a confirmation link. Create becomes "I've opened the link",
    /// and "Send the link again" comes on screen under it. Both go back once
    /// the account is real.
    /// </summary>
    bool Pending
    {
        get
        {
            SupabaseAuth auth = SupabaseAuth.Instance;
            return auth != null && auth.IsAnonymous && !string.IsNullOrEmpty(auth.PendingEmail);
        }
    }

    static void Relabel(Button button, string key)
    {
        LocalizedLabel label = button != null ? button.GetComponentInChildren<LocalizedLabel>(true) : null;

        if (label != null && label.key != key)
        {
            label.key = key;
            label.Apply();
        }
    }

    void DrawPending()
    {
        bool pending = Pending;

        Relabel(signUpButton, pending ? "account.confirmedIt" : "account.create");

        if (resendButton != null)
            resendButton.gameObject.SetActive(pending);

        if (pending && !_busy && statusLabel != null && _screen == AuthScreen.SignUp)
        {
            statusLabel.text = string.Format(AppStrings.Get("account.checkEmail"),
                                             SupabaseAuth.Instance.PendingEmail);
            statusLabel.color = ThemeManager.Get(ThemeRole.StatusGood);
            statusLabel.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Back from the browser, probably from opening the link: look once,
    /// quietly. Only a success says anything; "not yet" is already on screen.
    /// </summary>
    void OnApplicationFocus(bool focused)
    {
        if (!focused || _busy || !Pending)
            return;

        SupabaseAuth auth = SupabaseAuth.Instance;
        auth.StartCoroutine(auth.CheckConfirmed(result =>
        {
            if (result == null)
                Done(null);
        }));
    }

    // ------------------------------------------------------------- actions

    void SignUp()
    {
        if (_busy)
            return;

        if (Pending)
        {
            SetBusy(true);
            Say("account.working");
            SupabaseAuth.Instance.StartCoroutine(SupabaseAuth.Instance.CheckConfirmed(Done));
            return;
        }

        string email = Value(emailField);
        string password = Value(passwordField);

        if (!Complete(email, password))
            return;

        // Eight, the same as the website. The server takes six, so the app
        // let people make a password the website would then call too short.
        if (password.Length < 8)
        {
            SayPoor("account.shortPassword");
            return;
        }

        string role = roleChoice != null ? roleChoice.Selected : null;
        string work = workChoice != null ? workChoice.Selected : null;

        if (!AccountRole.IsValid(role))
        {
            SayUnder(roleError, "err.role");
            return;
        }

        HideRoleError();

        // Only a creator is asked what they do; a viewer sends no work at all.
        if (role == AccountRole.Viewer)
        {
            work = null;
        }
        else if (!LineOfWork.IsValid(work))
        {
            SayUnder(workError, "err.work");
            return;
        }

        HideWorkError();

        SupabaseAuth auth = SupabaseAuth.Instance;
        if (auth == null)
            return;

        SetBusy(true);
        Say("account.working");
        // On the auth service, not on this view.
        //
        // Sign-up refreshes the session half way through, and the account screen
        // shown after the tutorial closes itself the moment the session changes.
        // Closing it switched this view off, and Unity stops every coroutine on
        // an object that is switched off - so the last step, writing the name to
        // the profile, never ran. The leaderboard read the profile and called the
        // new walker "Player". The auth service is never switched off.
        auth.StartCoroutine(auth.SignUp(email, password, Value(nameField), role, work, Done));
    }

    /// <summary>"Send the link again", while one is out.</summary>
    void Resend()
    {
        if (_busy || !Pending)
            return;

        SetBusy(true);
        Say("account.working");
        SupabaseAuth.Instance.StartCoroutine(SupabaseAuth.Instance.ResendConfirmation(Done));
    }

    void SignIn()
    {
        if (_busy)
            return;

        // The sign-in screen's own boxes; a view built without them (an older
        // scene) signs in with the shared ones.
        string email = Value(signInEmailField != null ? signInEmailField : emailField);
        string password = Value(signInPasswordField != null ? signInPasswordField : passwordField);

        if (!Complete(email, password))
            return;

        SupabaseAuth auth = SupabaseAuth.Instance;
        if (auth == null)
            return;

        SetBusy(true);
        Say("account.working");
        auth.StartCoroutine(auth.SignIn(email, password, Done));
    }

    void SaveName()
    {
        if (_busy)
            return;

        SupabaseAuth auth = SupabaseAuth.Instance;
        if (auth == null)
            return;

        SetBusy(true);
        Say("account.working");
        auth.StartCoroutine(auth.SetDisplayName(Value(signedInNameField), Done));
    }

    public void SignOut()
    {
        if (_busy)
            return;

        SupabaseAuth auth = SupabaseAuth.Instance;
        if (auth == null)
            return;

        SetBusy(true);
        Say("account.working");
        auth.StartCoroutine(auth.SignOut(() =>
        {
            SetBusy(false);
            Say(null);
            Clear();
            Refresh();
        }));
    }

    /// <summary>
    /// The reply from any of them.
    ///
    /// A message means it failed and the message is GoTrue's own words wherever
    /// it wrote any - "Password should be at least 6 characters" is something a
    /// person can act on, and "sign-up failed" is not.
    /// </summary>
    void Done(string error)
    {
        SetBusy(false);

        // Not failures: the link went out, went out again, or has not been
        // opened yet. Said in the good colour or the plain one, never the red.
        if (error == "account.checkEmail" || error == "account.resent" || error == "account.notYet")
        {
            Refresh();
            TextMeshProUGUI status = Status;
            if (error != "account.checkEmail" && status != null)
            {
                status.text = AppStrings.Get(error);
                status.color = ThemeManager.Get(error == "account.resent"
                    ? ThemeRole.StatusGood : ThemeRole.StatusFair);
                status.gameObject.SetActive(true);
            }
            return;
        }

        if (string.IsNullOrEmpty(error))
        {
            Say(null);
            Clear();
            Refresh();
            return;
        }

        // A key if it is one of ours; the server's commonest English sentences
        // in the reader's language; anything rarer as the server wrote it.
        string key = error.StartsWith("auth.") || error.StartsWith("account.") ||
                     error.StartsWith("err.") ? error
                   : error.IndexOf("Invalid login", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "account.badLogin"
                   : error.IndexOf("already", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "account.taken"
                   : error.IndexOf("at least", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "account.shortPassword"
                   : error.IndexOf("not confirmed", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "account.unconfirmed"
                   : error.IndexOf("rate limit", System.StringComparison.OrdinalIgnoreCase) >= 0 ? "account.slowDown"
                   : null;

        TextMeshProUGUI line = Status;
        if (line == null)
            return;

        line.text = key != null ? AppStrings.Get(key) : error;
        line.color = ThemeManager.Get(ThemeRole.StatusPoor);
        line.gameObject.SetActive(true);
    }

    bool Complete(string email, string password)
    {
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
            return true;

        SayPoor("account.needBoth");
        return false;
    }

    void SetBusy(bool busy)
    {
        _busy = busy;

        if (signUpButton != null)
            signUpButton.interactable = !busy;

        if (signInButton != null)
            signInButton.interactable = !busy;

        if (resendButton != null)
            resendButton.interactable = !busy;

        if (signOutButton != null)
            signOutButton.interactable = !busy;

        if (saveNameButton != null)
            saveNameButton.interactable = !busy;
    }

    /// <summary>The status line of the screen that is up.</summary>
    TextMeshProUGUI Status
    {
        get
        {
            if (_screen == AuthScreen.SignIn && signInStatusLabel != null && !Named)
                return signInStatusLabel;

            return statusLabel;
        }
    }

    void Say(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            // Both, so nothing said on one screen is waiting on the other.
            Hide(statusLabel);
            Hide(signInStatusLabel);
            return;
        }

        TextMeshProUGUI line = Status;
        if (line == null)
            return;

        line.text = AppStrings.Get(key);
        line.color = ThemeManager.Get(ThemeRole.TextMuted);
        line.gameObject.SetActive(true);
    }

    void SayPoor(string key)
    {
        Say(key);

        TextMeshProUGUI line = Status;
        if (line != null)
            line.color = ThemeManager.Get(ThemeRole.StatusPoor);
    }

    static void Hide(TextMeshProUGUI line)
    {
        if (line == null)
            return;

        line.text = string.Empty;
        line.gameObject.SetActive(false);
    }

    void Clear()
    {
        if (emailField != null)
            emailField.text = string.Empty;

        if (nameField != null)
            nameField.text = string.Empty;

        if (signInEmailField != null)
            signInEmailField.text = string.Empty;

        if (workChoice != null)
            workChoice.Select(null);

        if (roleChoice != null)
            roleChoice.Select(null);

        HideWorkError();
        HideRoleError();
        ShowBanner();

        // Always, and not only on success. A password left sitting in a field on
        // a phone that is about to be handed to somebody is the reason.
        if (passwordField != null)
            passwordField.text = string.Empty;

        if (signInPasswordField != null)
            signInPasswordField.text = string.Empty;
    }

    static string Value(TMP_InputField field)
    {
        return field != null ? field.text.Trim() : string.Empty;
    }
}
