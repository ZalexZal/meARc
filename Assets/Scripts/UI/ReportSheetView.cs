using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// Reporting a stop, from the street.
///
/// The reviewer's inbox on the website has been able to show reports since
/// 0020, and nothing could file one: the people who find a marker in somebody's
/// garden are the people walking past it, and they are holding the app.
///
/// A reason and nothing else. There is a free-text field in report_marker and
/// this does not offer it, deliberately - the person using this is standing in
/// a street, possibly somewhere they would rather not be, and six taps beats a
/// paragraph. The reason plus the coordinates the database snapshots is enough
/// for an organiser to walk out and look.
///
/// The request runs from here rather than from a service because the panel is
/// open for the whole of it. There is nothing to keep alive afterwards.
/// </summary>
public class ReportSheetView : MonoBehaviour
{
    /// <summary>The reason column's check constraint, in the order they are drawn.</summary>
    public static readonly string[] Reasons =
    {
        "private_property", "unsafe", "offensive", "wrong_place", "spam", "other",
    };

    [Tooltip("One per reason, in the order of Reasons.")]
    public Button[] reasonButtons = new Button[6];

    public TextMeshProUGUI[] reasonLabels = new TextMeshProUGUI[6];

    public TextMeshProUGUI titleLabel;
    public TextMeshProUGUI statusLabel;
    public Button cancelButton;

    [Tooltip("Blocks whoever made the thing on show. Two taps: the first says " +
             "what blocking does, the second does it.")]
    public Button blockButton;
    public TextMeshProUGUI blockLabel;

    /// <summary>Raised after a block lands, so screens drop what they were showing.</summary>
    public static event Action OnBlocked;

    /// <summary>What is being reported. Exactly one of these is set.</summary>
    string _markerId;
    string _worldId;
    string _teamId;
    bool _busy;
    bool _blockArmed;

    /// <summary>
    /// Bumped by every Open. The "sent" message waits a moment before closing
    /// itself, and without this that timer would close a panel somebody had
    /// meanwhile reopened for a different stop.
    /// </summary>
    int _generation;

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Relabel;
        Relabel();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Relabel;
    }

    void Awake()
    {
        for (int i = 0; i < reasonButtons.Length; i++)
        {
            if (reasonButtons[i] == null)
                continue;

            // Captured per iteration, or every button reports the last reason.
            int index = i;
            reasonButtons[i].onClick.AddListener(delegate { Send(index); });
        }

        if (cancelButton != null)
            cancelButton.onClick.AddListener(Close);

        if (blockButton != null)
            blockButton.onClick.AddListener(Block);

        // Deliberately not deactivating here. The builder ships this panel off,
        // so Awake runs on the first Open - and closing it from Awake would shut
        // the panel in the same frame it was asked for.
    }

    /// <summary>Opens the chooser for one marker.</summary>
    public void Open(string markerId)
    {
        if (string.IsNullOrEmpty(markerId))
            return;

        _worldId = null;
        _teamId = null;
        _markerId = markerId;
        Show();
    }

    /// <summary>
    /// Opens it for a whole trail.
    ///
    /// report_world has been there since 0020 and nothing has ever called it: a
    /// walker could report one marker in somebody's garden and not a trail that
    /// was entirely somewhere it should not be. The reviewer's queue has always
    /// been able to show these and remove_world to act on them.
    /// </summary>
    public void OpenTrail(string worldId)
    {
        if (string.IsNullOrEmpty(worldId))
            return;

        _markerId = null;
        _teamId = null;
        _worldId = worldId;
        Show();
    }

    /// <summary>
    /// Opens it for a team's name, from a row on the board.
    ///
    /// A team name is the one thing on the board somebody else typed, so it is
    /// the one thing on it that can be offensive. Three of the six reasons are
    /// about a place, and a name has no place, so they are not offered.
    /// </summary>
    public void OpenTeam(string teamId)
    {
        if (string.IsNullOrEmpty(teamId))
            return;

        _markerId = null;
        _worldId = null;
        _teamId = teamId;
        Show();
    }

    void Show()
    {
        _busy = false;
        _blockArmed = false;
        _generation++;

        Say(null);
        SetInteractable(true);
        Relabel();

        // Nobody blocks themselves. A creator reporting a stop on their own
        // trail - to test the button, most likely - gets the reasons only.
        if (blockButton != null)
        {
            TrailService trails = TrailService.Instance;
            bool own = string.IsNullOrEmpty(_teamId) && trails != null &&
                       trails.Selected != null && trails.Selected.role == "owner";
            blockButton.gameObject.SetActive(!own);
        }

        gameObject.SetActive(true);
    }

    static bool AboutAPlace(string reason)
    {
        return reason == "private_property" || reason == "unsafe" || reason == "wrong_place";
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    void Relabel()
    {
        bool team = !string.IsNullOrEmpty(_teamId);

        if (titleLabel != null)
        {
            titleLabel.text = AppStrings.Get(team ? "report.titleTeam"
                : string.IsNullOrEmpty(_markerId) ? "report.titleTrail"
                : "report.title");
        }

        for (int i = 0; i < reasonLabels.Length && i < Reasons.Length; i++)
        {
            if (reasonLabels[i] != null)
            {
                reasonLabels[i].text = AppStrings.Get("report.reason." +
                    (team && Reasons[i] == "offensive" ? "offensiveName" : Reasons[i]));
            }

            if (i < reasonButtons.Length && reasonButtons[i] != null)
                reasonButtons[i].gameObject.SetActive(!(team && AboutAPlace(Reasons[i])));
        }

        if (blockLabel != null)
        {
            blockLabel.text = AppStrings.Get(_blockArmed ? "block.confirm"
                : team ? "block.team" : "block.creator");
        }
    }

    void Send(int index)
    {
        bool haveSomething = !string.IsNullOrEmpty(_markerId) || !string.IsNullOrEmpty(_worldId) ||
                             !string.IsNullOrEmpty(_teamId);

        if (_busy || index < 0 || index >= Reasons.Length || !haveSomething)
            return;

        _busy = true;
        SetInteractable(false);
        Say("report.sending", ThemeRole.TextMuted);

        StartCoroutine(Post(Reasons[index], _generation));
    }

    IEnumerator Post(string reason, int generation)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            Failed("report.offline");
            yield break;
        }

        string rpc, key, id;

        if (!string.IsNullOrEmpty(_teamId)) { rpc = "report_team"; key = "p_team"; id = _teamId; }
        else if (!string.IsNullOrEmpty(_markerId)) { rpc = "report_marker"; key = "p_marker"; id = _markerId; }
        else { rpc = "report_world"; key = "p_world"; id = _worldId; }

        string body = "{\"" + key + "\":\"" + Escape(id) + "\"," +
                      "\"p_reason\":\"" + Escape(reason) + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/" + rpc), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[Report] report_marker failed (" + request.responseCode + "): " +
                                 (request.downloadHandler != null ? request.downloadHandler.text : ""));
                string said = Postgrest.Said(request);

                if (!string.IsNullOrWhiteSpace(said))
                {
                    _busy = false;
                    SetInteractable(true);

                    if (statusLabel != null)
                    {
                        statusLabel.text = said;
                        statusLabel.color = ThemeManager.Get(ThemeRole.StatusPoor);
                        statusLabel.gameObject.SetActive(true);
                    }
                }
                else
                {
                    Failed("report.failed");
                }

                yield break;
            }
        }

        // Said here rather than by closing straight away: a report that vanishes
        // without a word reads as a tap that missed.
        Say("report.sent", ThemeRole.StatusGood);
        _busy = false;

        yield return new WaitForSeconds(1.6f);

        // Only if nobody has reopened it for a different stop in the meantime.
        if (_generation == generation)
            Close();
    }

    // ------------------------------------------------------------------ blocking

    /// <summary>
    /// Blocks whoever made what is on show.
    ///
    /// Two taps, like leaving a team. Blocking takes you out of their trails,
    /// which on a trail you are halfway round is a real loss, so the first tap
    /// says that in words and only the second one does it.
    /// </summary>
    void Block()
    {
        if (_busy)
            return;

        if (!_blockArmed)
        {
            _blockArmed = true;
            Relabel();
            return;
        }

        string kind, id;

        if (!string.IsNullOrEmpty(_teamId)) { kind = "team"; id = _teamId; }
        else if (!string.IsNullOrEmpty(_markerId)) { kind = "marker"; id = _markerId; }
        else if (!string.IsNullOrEmpty(_worldId)) { kind = "trail"; id = _worldId; }
        else return;

        _busy = true;
        SetInteractable(false);
        Say("report.sending", ThemeRole.TextMuted);
        StartCoroutine(PostBlock(kind, id, _generation));
    }

    IEnumerator PostBlock(string kind, string id, int generation)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            _blockArmed = false;
            Relabel();
            Failed("report.offline");
            yield break;
        }

        string body = "{\"p_kind\":\"" + kind + "\",\"p_target\":\"" + Escape(id) + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/block_user"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string said = Postgrest.Said(request) ?? string.Empty;
                Debug.LogWarning("[Report] block_user failed (" + request.responseCode + "): " + said);

                _blockArmed = false;
                Relabel();

                // Two refusals a person can act on; anything else is the connection.
                Failed(said.Contains("that is you") ? "block.self"
                       : said.Contains("nobody") ? "block.nobody"
                       : "report.failed");
                yield break;
            }
        }

        _blockArmed = false;
        Relabel();
        Say("block.done", ThemeRole.StatusGood);

        // Their trails are gone from the list now, and so may be the one on
        // screen. Asked again rather than predicted.
        if (TrailService.Instance != null)
            TrailService.Instance.StartCoroutine(TrailService.Instance.RefreshMine());

        if (OnBlocked != null)
            OnBlocked();

        yield return new WaitForSeconds(1.6f);

        _busy = false;

        if (_generation == generation)
            Close();
    }

    void Failed(string key)
    {
        _busy = false;
        SetInteractable(true);
        Say(key, ThemeRole.StatusPoor);
    }

    void SetInteractable(bool on)
    {
        for (int i = 0; i < reasonButtons.Length; i++)
        {
            if (reasonButtons[i] != null)
                reasonButtons[i].interactable = on;
        }

        if (blockButton != null)
            blockButton.interactable = on;
    }

    void Say(string key, ThemeRole role = ThemeRole.TextMuted)
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
        statusLabel.color = ThemeManager.Get(role);
        statusLabel.gameObject.SetActive(true);
    }

    static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
