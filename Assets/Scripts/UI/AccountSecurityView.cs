using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Changing the email address and the password, on their own page.
///
/// The same two things the website's account settings do, with the same rule
/// for the password: prove the current one first.
/// </summary>
public class AccountSecurityView : MonoBehaviour
{
    public TextMeshProUGUI currentEmailLabel;
    public TMP_InputField newEmailField;
    public Button emailButton;

    public TMP_InputField currentPasswordField;
    public TMP_InputField newPasswordField;
    public TMP_InputField againPasswordField;
    public Button passwordButton;

    public TextMeshProUGUI statusLabel;

    bool _busy;

    void Awake()
    {
        if (emailButton != null)
            emailButton.onClick.AddListener(ChangeEmail);

        if (passwordButton != null)
            passwordButton.onClick.AddListener(ChangePassword);
    }

    void OnEnable()
    {
        Say(null, ThemeRole.TextMuted);
        Draw();
        SupabaseAuth.OnSessionChanged += Draw;
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= Draw;
    }

    void Draw()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;

        if (currentEmailLabel != null)
            currentEmailLabel.text = string.Format(AppStrings.Get("security.current"),
                                                   auth != null ? auth.Email : string.Empty);
    }

    void ChangeEmail()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;
        string email = newEmailField != null ? newEmailField.text.Trim() : string.Empty;

        if (_busy || auth == null)
            return;

        if (email.Length < 5 || !email.Contains("@"))
        {
            Say("security.emailInvalid", ThemeRole.StatusPoor);
            return;
        }

        _busy = true;
        Say("account.working", ThemeRole.TextMuted);

        // On the auth service: this page may be left before the reply lands.
        auth.StartCoroutine(auth.ChangeEmail(email, error =>
        {
            _busy = false;

            if (error == "account.emailSent")
            {
                Say(error, ThemeRole.StatusGood);
                return;
            }

            if (!string.IsNullOrEmpty(error))
            {
                Say(error, ThemeRole.StatusPoor);
                return;
            }

            if (newEmailField != null)
                newEmailField.text = string.Empty;

            Draw();
            Say("account.emailDone", ThemeRole.StatusGood);
        }));
    }

    void ChangePassword()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;

        if (_busy || auth == null)
            return;

        string current = currentPasswordField != null ? currentPasswordField.text : string.Empty;
        string next = newPasswordField != null ? newPasswordField.text : string.Empty;
        string again = againPasswordField != null ? againPasswordField.text : string.Empty;

        if (next.Length < 8)
        {
            Say("account.pwShort", ThemeRole.StatusPoor);
            return;
        }

        if (next != again)
        {
            Say("account.pwMismatch", ThemeRole.StatusPoor);
            return;
        }

        _busy = true;
        Say("account.working", ThemeRole.TextMuted);

        auth.StartCoroutine(auth.ChangePassword(current, next, error =>
        {
            _busy = false;

            if (!string.IsNullOrEmpty(error))
            {
                Say(error, ThemeRole.StatusPoor);
                return;
            }

            if (currentPasswordField != null) currentPasswordField.text = string.Empty;
            if (newPasswordField != null) newPasswordField.text = string.Empty;
            if (againPasswordField != null) againPasswordField.text = string.Empty;

            Say("account.pwDone", ThemeRole.StatusGood);
        }));
    }

    void Say(string keyOrText, ThemeRole role)
    {
        if (statusLabel == null)
            return;

        bool show = !string.IsNullOrEmpty(keyOrText);
        statusLabel.gameObject.SetActive(show);

        if (!show)
            return;

        // Ours are keys; the auth server's own sentences are shown as it wrote them.
        statusLabel.text = keyOrText.StartsWith("account.") || keyOrText.StartsWith("security.") ||
                           keyOrText.StartsWith("auth.")
            ? AppStrings.Get(keyOrText) : keyOrText;
        statusLabel.color = ThemeManager.Get(role);
    }
}
