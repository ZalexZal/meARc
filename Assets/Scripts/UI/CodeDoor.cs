using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Enter a code: the six boxes, and what happens after they are full.
///
/// Redeeming a code needs an identity, and on a first run the anonymous sign-in
/// may still be in flight when the reader finishes typing. Rather than refuse and
/// make them press Join twice, this waits for the session and then goes.
/// </summary>
public class CodeDoor : MonoBehaviour
{
    [Header("Input")]
    public CodeBoxes boxes;

    [Header("Controls")]
    public Button joinButton;
    public TextMeshProUGUI joinLabel;
    public Button cancelButton;

    [Header("Feedback")]
    [Tooltip("Wrong code, no signal, or waiting to be signed in. Empty otherwise.")]
    public TextMeshProUGUI message;

    bool _busy;

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += RefreshLabels;
        RefreshLabels();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= RefreshLabels;
    }

    /// <summary>Called by DoorFlow each time this screen is opened.</summary>
    public void Begin()
    {
        _busy = false;

        if (boxes != null)
        {
            boxes.Clear();
            boxes.Focus();
        }

        SetMessage(string.Empty);
        RefreshLabels();
    }

    public void Cancel()
    {
        if (DoorFlow.Instance != null)
            DoorFlow.Instance.CloseCode();
    }

    public void Join()
    {
        if (_busy || boxes == null)
            return;

        if (!boxes.IsComplete)
        {
            boxes.Focus();
            return;
        }

        TrailService service = TrailService.Instance;
        if (service == null)
        {
            SetMessage(AppStrings.Get("code.failed"));
            return;
        }

        SupabaseAuth auth = SupabaseAuth.Instance;

        // join_world raises for a caller with no auth.uid(), so a code typed in the
        // first second of a first run would come back "unknown code" - which is a
        // lie about the code. Say what is actually happening instead.
        if (auth != null && !auth.IsSignedIn)
        {
            SetMessage(AppStrings.Get("code.needsAccount"));

            if (!auth.HasSettled)
                return;
        }

        _busy = true;
        SetBusyLabel(true);
        SetMessage(string.Empty);

        StartCoroutine(service.Join(boxes.Code, Done));
    }

    void Done(bool joined, string detail)
    {
        _busy = false;
        SetBusyLabel(false);

        if (joined)
        {
            if (DoorFlow.Instance != null)
                DoorFlow.Instance.Entered();

            return;
        }

        // "unknown" is the reader's mistake and everything else is ours. Saying
        // "no trail with that code" when the server is unreachable sends somebody
        // to argue with whoever gave them a perfectly good code.
        SetMessage(AppStrings.Get(detail == "unknown" ? "code.unknown" : "code.failed"));

        if (boxes != null)
            boxes.Focus();
    }

    void SetBusyLabel(bool busy)
    {
        if (joinLabel != null)
            joinLabel.text = AppStrings.Get(busy ? "code.joining" : "code.join");

        if (joinButton != null)
            joinButton.interactable = !busy;
    }

    void SetMessage(string text)
    {
        if (message == null)
            return;

        message.text = text;
        message.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    void RefreshLabels()
    {
        SetBusyLabel(_busy);
    }
}
