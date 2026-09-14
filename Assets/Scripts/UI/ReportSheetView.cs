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

    /// <summary>What is being reported. One of these is set, never both.</summary>
    string _markerId;
    string _worldId;
    bool _busy;

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
        _markerId = markerId;
        _busy = false;
        _generation++;

        Say(null);
        SetInteractable(true);
        gameObject.SetActive(true);
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
        _worldId = worldId;
        _busy = false;
        _generation++;

        Say(null);
        SetInteractable(true);
        gameObject.SetActive(true);
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    void Relabel()
    {
        if (titleLabel != null)
        {
            titleLabel.text = AppStrings.Get(string.IsNullOrEmpty(_markerId)
                ? "report.titleTrail"
                : "report.title");
        }

        for (int i = 0; i < reasonLabels.Length && i < Reasons.Length; i++)
        {
            if (reasonLabels[i] != null)
                reasonLabels[i].text = AppStrings.Get("report.reason." + Reasons[i]);
        }
    }

    void Send(int index)
    {
        bool haveSomething = !string.IsNullOrEmpty(_markerId) || !string.IsNullOrEmpty(_worldId);

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

        bool whole = string.IsNullOrEmpty(_markerId);

        string body = whole
            ? "{\"p_world\":\"" + Escape(_worldId) + "\"," +
              "\"p_reason\":\"" + Escape(reason) + "\"}"
            : "{\"p_marker\":\"" + Escape(_markerId) + "\"," +
              "\"p_reason\":\"" + Escape(reason) + "\"}";

        string rpc = whole ? "report_world" : "report_marker";

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
