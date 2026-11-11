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

    // LAB: one code box. A code that is not a trail's may be a team's; then the
    // trail is joined first and the team straight after (lab_trail_code_for_team).
    bool _triedTeam;
    string _teamCode;

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
        _triedTeam = false;

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

        _triedTeam = false;
        StartCoroutine(service.Join(boxes.Code, Done));
    }

    void Done(bool joined, string detail)
    {
        if (!joined && detail == "banned")
        {
            _busy = false;
            SetBusyLabel(false);
            SetMessage(AppStrings.Get("teacher.err.banned"));
            TeacherService.SayBanned();
            return;
        }

        if (!joined && detail == "unknown" && !_triedTeam && boxes != null)
        {
            _triedTeam = true;
            // On the trail service, not on this screen: joining the trail
            // selects it, the door closes, and a coroutine on a closed screen
            // stops before it reaches the team (seen on the phone, 5 Nov).
            TrailService.Instance.StartCoroutine(TryTeamCode(boxes.Code));
            return;
        }

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
        bool theirs = detail == "unknown";

        // Refused because a parent has not agreed yet: say so, and ask again so
        // the parent door takes over from here.
        if (detail == "parent")
        {
            SetMessage(AppStrings.Get("code.parent"));

            if (ConsentService.Instance != null)
                ConsentService.Instance.Refresh();

            return;
        }

        SetMessage(AppStrings.Get(theirs ? "code.unknown" : "code.failed"));

        if (boxes == null)
            return;

        // And the same distinction decides whether the boxes are emptied.
        //
        // A rejected code has to be retyped, and leaving the wrong six characters
        // sitting there means deleting them one at a time before you can. But if
        // the server was simply unreachable then the code was probably right, and
        // wiping it would make somebody type it again for a fault that was not
        // theirs - they only need to press Join.
        if (theirs)
            boxes.Clear();

        boxes.Focus();
    }

    /// <summary>The code as a team's: its trail, joined, then the team itself.</summary>
    System.Collections.IEnumerator TryTeamCode(string code)
    {
        string trailCode = null;
        yield return TrailService.Instance.TrailCodeForTeam(code, c => trailCode = c);

        if (string.IsNullOrEmpty(trailCode))
        {
            Done(false, "unknown");
            yield break;
        }

        _teamCode = code;
        yield return TrailService.Instance.Join(trailCode, JoinedForTeam);
    }

    void JoinedForTeam(bool joined, string detail)
    {
        if (!joined || TeamService.Instance == null)
        {
            Done(joined, detail);
            return;
        }

        // In the trail. The team next; if that is refused (full, a guest on a
        // game) the walker is still in the trail and the team door explains why.
        TeamService.Instance.Join(_teamCode, error =>
        {
            if (!string.IsNullOrEmpty(error))
                Debug.LogWarning("[Code] Joined the trail, not the team: " + error);
            if (TeacherService.IsBanned(error))
                TeacherService.SayBanned();

            Done(true, detail);
        });
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
