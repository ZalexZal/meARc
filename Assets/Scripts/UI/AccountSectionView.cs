using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The account card in Settings: sign up, sign in, sign out.
///
/// Two states in one card, because they are the same subject and a reader
/// arriving here is asking one question - "is my walking saved anywhere?" - and
/// the answer differs only in which half is on screen.
///
/// Signing up converts the anonymous user in place rather than making a second
/// one. That is <see cref="SupabaseAuth.SignUp"/>'s job; what this class owes it
/// is never to offer the button while a call is already running, because two
/// PUTs racing on one session is a good way to end up signed in as nobody.
/// </summary>
public class AccountSectionView : MonoBehaviour
{
    [Header("Anonymous")]
    public GameObject anonymousGroup;

    [Tooltip("What the leaderboard calls this person. Without it ensure_team " +
             "falls back to \"Player\" and four characters of their id, and a " +
             "board of six walkers is six rows nobody can tell apart.")]
    public TMP_InputField nameField;

    public TMP_InputField emailField;
    public TMP_InputField passwordField;

    [Tooltip("Broker, teacher, guide or other. Required to make an account, with " +
             "nothing chosen to begin with; sign-in ignores it.")]
    public WorkChoiceView workChoice;

    [Tooltip("\"Choose your line of work.\", right under the choices, so it is " +
             "read where the answer is missing rather than at the foot of the card.")]
    public TextMeshProUGUI workError;

    public Button signUpButton;
    public Button signInButton;

    [Header("Signed in")]
    public GameObject signedInGroup;
    public TextMeshProUGUI emailLabel;
    public TMP_InputField signedInNameField;
    public Button saveNameButton;
    public Button signOutButton;

    [Header("Both")]
    [Tooltip("Says what went wrong, or that something is happening. Empty when idle.")]
    public TextMeshProUGUI statusLabel;

    bool _busy;

    void OnEnable()
    {
        if (signUpButton != null)
        {
            signUpButton.onClick.RemoveAllListeners();
            signUpButton.onClick.AddListener(SignUp);
        }

        if (signInButton != null)
        {
            signInButton.onClick.RemoveAllListeners();
            signInButton.onClick.AddListener(SignIn);
        }

        if (signOutButton != null)
        {
            signOutButton.onClick.RemoveAllListeners();
            signOutButton.onClick.AddListener(SignOut);
        }

        if (saveNameButton != null)
        {
            saveNameButton.onClick.RemoveAllListeners();
            saveNameButton.onClick.AddListener(SaveName);
        }

        SupabaseAuth.OnSessionChanged += Refresh;
        AppStrings.OnLanguageChanged += Refresh;

        if (workChoice != null)
            workChoice.OnChosen += HideWorkError;

        // On the way in as well as on the event: this card lives on the Settings
        // panel, which is switched off for most of the app's life, so it is
        // reliably absent when the session actually changes.
        Refresh();
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;

        if (workChoice != null)
            workChoice.OnChosen -= HideWorkError;
    }

    void HideWorkError()
    {
        if (workError != null)
            workError.gameObject.SetActive(false);
    }

    /// <summary>Shows whichever half matches the session.</summary>
    public void Refresh()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;
        bool named = auth != null && auth.IsSignedIn && !auth.IsAnonymous;

        if (anonymousGroup != null)
            anonymousGroup.SetActive(!named);

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

        SetBusy(_busy);
        DrawPending();
    }

    /// <summary>
    /// Waiting on a confirmation link. The two buttons change job rather than
    /// the screen growing new ones: Create becomes "I've opened the link", and
    /// Sign in becomes "Send it again". Both go back once the account is real.
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
        Relabel(signInButton, pending ? "account.resend" : "account.signIn");

        if (pending && !_busy && statusLabel != null)
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
            Say("account.shortPassword");
            statusLabel.color = ThemeManager.Get(ThemeRole.StatusPoor);
            return;
        }

        string work = workChoice != null ? workChoice.Selected : null;

        if (!LineOfWork.IsValid(work))
        {
            if (workError != null)
            {
                Say(null);
                workError.text = AppStrings.Get("err.work");
                workError.gameObject.SetActive(true);
            }
            else
            {
                Say("err.work");
                statusLabel.color = ThemeManager.Get(ThemeRole.StatusPoor);
            }
            return;
        }

        HideWorkError();

        SupabaseAuth auth = SupabaseAuth.Instance;
        if (auth == null)
            return;

        SetBusy(true);
        Say("account.working");
        // On the auth service, not on this card.
        //
        // Sign-up refreshes the session half way through, and the account screen
        // shown after the tutorial closes itself the moment the session changes.
        // Closing it switched this card off, and Unity stops every coroutine on
        // an object that is switched off - so the last step, writing the name to
        // the profile, never ran. The leaderboard read the profile and called the
        // new walker "Player". The auth service is never switched off.
        auth.StartCoroutine(auth.SignUp(email, password, Value(nameField), work, Done));
    }

    void SignIn()
    {
        if (_busy)
            return;

        if (Pending)
        {
            SetBusy(true);
            Say("account.working");
            SupabaseAuth.Instance.StartCoroutine(SupabaseAuth.Instance.ResendConfirmation(Done));
            return;
        }

        string email = Value(emailField);
        string password = Value(passwordField);

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
    /// The reply from any of the three.
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
            if (error != "account.checkEmail" && statusLabel != null)
            {
                statusLabel.text = AppStrings.Get(error);
                statusLabel.color = ThemeManager.Get(error == "account.resent"
                    ? ThemeRole.StatusGood : ThemeRole.StatusFair);
                statusLabel.gameObject.SetActive(true);
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

        statusLabel.text = key != null ? AppStrings.Get(key) : error;

        statusLabel.color = ThemeManager.Get(ThemeRole.StatusPoor);
        statusLabel.gameObject.SetActive(true);
    }

    bool Complete(string email, string password)
    {
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
            return true;

        Say("account.needBoth");
        statusLabel.color = ThemeManager.Get(ThemeRole.StatusPoor);
        return false;
    }

    void SetBusy(bool busy)
    {
        _busy = busy;

        if (signUpButton != null)
            signUpButton.interactable = !busy;

        if (signInButton != null)
            signInButton.interactable = !busy;

        if (signOutButton != null)
            signOutButton.interactable = !busy;

        if (saveNameButton != null)
            saveNameButton.interactable = !busy;
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

    void Clear()
    {
        if (emailField != null)
            emailField.text = string.Empty;

        if (nameField != null)
            nameField.text = string.Empty;

        if (workChoice != null)
            workChoice.Select(null);

        HideWorkError();

        // Always, and not only on success. A password left sitting in a field on
        // a phone that is about to be handed to somebody is the reason.
        if (passwordField != null)
            passwordField.text = string.Empty;
    }

    static string Value(TMP_InputField field)
    {
        return field != null ? field.text.Trim() : string.Empty;
    }
}
