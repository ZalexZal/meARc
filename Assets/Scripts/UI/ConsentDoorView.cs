using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The door an under-14 meets instead of the trails: ask a parent, then wait.
///
/// Two halves on one card. With no request yet, it asks for the parent's email
/// and sends the link. Once one is out, it says where it went and offers to
/// look again, to send it again or to a different address, and the demo in
/// the meantime - the one part of the app that sends nothing anywhere.
/// </summary>
public class ConsentDoorView : MonoBehaviour
{
    [Header("Asking")]
    public GameObject askGroup;
    public TMP_InputField emailField;
    public Button sendButton;

    [Header("Waiting")]
    public GameObject waitGroup;
    public TextMeshProUGUI waitLabel;
    public Button checkButton;
    public Button changeButton;

    [Header("Both")]
    public TextMeshProUGUI statusLabel;

    bool _busy;
    bool _changing;

    void Awake()
    {
        if (sendButton != null) sendButton.onClick.AddListener(Send);
        if (checkButton != null) checkButton.onClick.AddListener(Check);
        if (changeButton != null) changeButton.onClick.AddListener(Change);

        // The demo and the age question are wired by the builder, straight to
        // DoorFlow, like every other way out of a door.
    }

    void OnEnable()
    {
        ConsentService.OnChanged += Draw;
        AppStrings.OnLanguageChanged += Draw;
        _changing = false;
        Say(null, ThemeRole.TextMuted);
        Draw();
    }

    void OnDisable()
    {
        ConsentService.OnChanged -= Draw;
        AppStrings.OnLanguageChanged -= Draw;
    }

    public void Refresh()
    {
        if (ConsentService.Instance != null)
            ConsentService.Instance.Refresh();

        Draw();
    }

    void Draw()
    {
        ConsentService consent = ConsentService.Instance;
        bool asked = consent != null && consent.State == "pending" && !_changing;

        if (askGroup != null) askGroup.SetActive(!asked);
        if (waitGroup != null) waitGroup.SetActive(asked);

        if (waitLabel != null && asked)
            waitLabel.text = string.Format(AppStrings.Get("consent.waiting"), consent.ParentEmail);

        if (emailField != null && _changing && string.IsNullOrEmpty(emailField.text) && consent != null)
            emailField.text = consent.ParentEmail ?? string.Empty;

        foreach (Button button in new[] { sendButton, checkButton, changeButton })
            if (button != null) button.interactable = !_busy;
    }

    void Send()
    {
        ConsentService consent = ConsentService.Instance;
        string email = emailField != null ? emailField.text.Trim() : string.Empty;

        if (_busy || consent == null)
            return;

        if (email.Length < 5 || !email.Contains("@") || !email.Contains("."))
        {
            Say("consent.badEmail", ThemeRole.StatusPoor);
            return;
        }

        _busy = true;
        Say("account.working", ThemeRole.TextMuted);
        Draw();

        consent.StartCoroutine(consent.Ask(email, error =>
        {
            _busy = false;
            _changing = false;

            if (error == null)
                Say("consent.sent", ThemeRole.StatusGood);
            else
                SayText(error, ThemeRole.StatusPoor);

            Draw();
        }));
    }

    void Check()
    {
        if (_busy)
            return;

        Say("consent.notYet", ThemeRole.TextMuted);
        Refresh();
    }

    /// <summary>Back to the address, filled in, to fix it or to send it again.</summary>
    void Change()
    {
        _changing = true;
        Say(null, ThemeRole.TextMuted);
        Draw();
    }

    void Say(string key, ThemeRole role)
    {
        SayText(string.IsNullOrEmpty(key) ? null : AppStrings.Get(key), role);
    }

    void SayText(string text, ThemeRole role)
    {
        if (statusLabel == null)
            return;

        statusLabel.gameObject.SetActive(!string.IsNullOrEmpty(text));
        statusLabel.text = text ?? string.Empty;
        statusLabel.color = ThemeManager.Get(role);
    }
}
