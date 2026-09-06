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

        // On the way in as well as on the event: this card lives on the Settings
        // panel, which is switched off for most of the app's life, so it is
        // reliably absent when the session actually changes.
        Refresh();
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= Refresh;
        AppStrings.OnLanguageChanged -= Refresh;
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
    }

    void SignUp()
    {
        if (_busy)
            return;

        string email = Value(emailField);
        string password = Value(passwordField);

        if (!Complete(email, password))
            return;

        SupabaseAuth auth = SupabaseAuth.Instance;
        if (auth == null)
            return;

        SetBusy(true);
        Say("account.working");
        StartCoroutine(auth.SignUp(email, password, Value(nameField), Done));
    }

    void SignIn()
    {
        if (_busy)
            return;

        string email = Value(emailField);
        string password = Value(passwordField);

        if (!Complete(email, password))
            return;

        SupabaseAuth auth = SupabaseAuth.Instance;
        if (auth == null)
            return;

        SetBusy(true);
        Say("account.working");
        StartCoroutine(auth.SignIn(email, password, Done));
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
        StartCoroutine(auth.SetDisplayName(Value(signedInNameField), Done));
    }

    void SignOut()
    {
        if (_busy)
            return;

        SupabaseAuth auth = SupabaseAuth.Instance;
        if (auth == null)
            return;

        SetBusy(true);
        Say("account.working");
        StartCoroutine(auth.SignOut(() =>
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

        if (string.IsNullOrEmpty(error))
        {
            Say(null);
            Clear();
            Refresh();
            return;
        }

        // A key if it is one of ours, otherwise the server's sentence as written.
        statusLabel.text = error.StartsWith("auth.") || error.StartsWith("account.")
            ? AppStrings.Get(error)
            : error;

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
