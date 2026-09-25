using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Deleting the account, from inside the app, in two taps.
///
/// GDPR asks that leaving be as easy as joining, and Google Play and the App
/// Store both require that an app which makes accounts can also delete them
/// without sending people somewhere else. Making an account here is four fields
/// and a button; deleting it is one button and one confirmation.
///
/// The confirmation says exactly what goes. It is the only friction, and it is
/// the kind that stops an accident rather than the kind that hides the exit.
/// </summary>
public class DeleteAccountView : MonoBehaviour
{
    public Button deleteButton;
    public TextMeshProUGUI deleteLabel;

    public GameObject confirmGroup;
    public TextMeshProUGUI confirmText;
    public Button confirmButton;
    public Button cancelButton;

    public TextMeshProUGUI statusLabel;

    bool _busy;

    void Awake()
    {
        if (deleteButton != null)
            deleteButton.onClick.AddListener(Ask);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(Cancel);

        if (confirmButton != null)
            confirmButton.onClick.AddListener(Delete);
    }

    void OnEnable()
    {
        SupabaseAuth.OnSessionChanged += Draw;
        AppStrings.OnLanguageChanged += Draw;
        Cancel();
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= Draw;
        AppStrings.OnLanguageChanged -= Draw;
    }

    void Draw()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;
        bool guest = auth == null || auth.IsAnonymous;

        if (deleteLabel != null)
            deleteLabel.text = AppStrings.Get(guest ? "delete.guest" : "delete.account");

        if (confirmText != null)
            confirmText.text = AppStrings.Get(guest ? "delete.guestConfirm" : "delete.confirm");
    }

    void Ask()
    {
        if (_busy)
            return;

        Draw();
        Say(null);

        if (confirmGroup != null)
            confirmGroup.SetActive(true);

        if (deleteButton != null)
            deleteButton.gameObject.SetActive(false);
    }

    void Cancel()
    {
        if (_busy)
            return;

        Draw();
        Say(null);

        if (confirmGroup != null)
            confirmGroup.SetActive(false);

        if (deleteButton != null)
            deleteButton.gameObject.SetActive(true);
    }

    void Delete()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;

        if (_busy || auth == null)
            return;

        _busy = true;
        Say(AppStrings.Get("delete.working"), ThemeRole.TextMuted);

        auth.StartCoroutine(auth.DeleteAccount(error =>
        {
            _busy = false;

            if (error == "account.deleteOnWebsite")
            {
                Say(string.Format(AppStrings.Get(error), SiteLink.Account()), ThemeRole.StatusFair);
                return;
            }

            if (!string.IsNullOrEmpty(error))
            {
                Say(error.StartsWith("account.") || error.StartsWith("auth.")
                    ? AppStrings.Get(error) : error, ThemeRole.StatusPoor);
                return;
            }

            Cancel();
            Say(AppStrings.Get("delete.done"), ThemeRole.StatusGood);

            if (ProfileService.Instance != null)
                ProfileService.Instance.Load();
        }));
    }

    void Say(string text, ThemeRole role = ThemeRole.TextMuted)
    {
        if (statusLabel == null)
            return;

        bool show = !string.IsNullOrEmpty(text);
        statusLabel.gameObject.SetActive(show);

        if (show)
        {
            statusLabel.text = text;
            statusLabel.color = ThemeManager.Get(role);
        }
    }
}

/// <summary>The website's pages the app points at, in the app's language.</summary>
public static class SiteLink
{
    static string Root
    {
        get
        {
            SupabaseConfig config = SupabaseConfig.Instance;
            string root = config != null && !string.IsNullOrEmpty(config.siteUrl)
                ? config.siteUrl : "https://artrails.netlify.app";
            return root.TrimEnd('/');
        }
    }

    static string Lang
    {
        get { return AppStrings.CurrentLanguage == AppLanguage.Bulgarian ? string.Empty : "/en"; }
    }

    public static string Terms() { return Root + Lang + "/terms/"; }
    public static string Privacy() { return Root + Lang + "/privacy/"; }
    public static string Account() { return Root + Lang + "/account/"; }

    /// <summary>Where an email confirmation link lands: "confirmed, go back to the app".</summary>
    public static string Confirmed() { return Root + Lang + "/confirmed/"; }

    /// <summary>The page a parent opens to agree, or not, to an under-14's account.</summary>
    public static string Consent() { return Root + Lang + "/consent/"; }
}
