using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// The hints on a marker, and what each one costs.
///
/// Everything behind this has existed since 0015 and nothing in the app has ever
/// touched it: the table, reveal_hint, the deduction inside points_after_hints,
/// and the rule that a hint's text does not travel until it has been paid for.
/// A creator could write a ladder of hints, price them, and no walker could ever
/// see one.
///
/// The price is the point of the screen. A hint nobody weighs is the answer with
/// an extra tap in front of it, so every row says what it costs before it opens,
/// and the card says what has already been spent.
///
/// The ladder is climbed from the bottom. A creator writes three hints as a
/// nudge, a stronger nudge and most of the answer, and prices them accordingly;
/// opening the last one first buys the answer at the price of a hint. 0030
/// refuses that on the server and this greys the button so nobody has to find
/// out by being refused.
/// </summary>
public class HintSheetView : MonoBehaviour
{
    /// <summary>More than this on one marker is a creator problem, not a UI one.</summary>
    public const int MaxRows = 6;

    [Header("Rows")]
    public RectTransform[] rows = new RectTransform[MaxRows];
    public Button[] rowButtons = new Button[MaxRows];
    public TextMeshProUGUI[] rowTitles = new TextMeshProUGUI[MaxRows];
    public TextMeshProUGUI[] rowChips = new TextMeshProUGUI[MaxRows];
    public GameObject[] rowChipHolders = new GameObject[MaxRows];
    public TextMeshProUGUI[] rowActions = new TextMeshProUGUI[MaxRows];
    public TextMeshProUGUI[] rowBodies = new TextMeshProUGUI[MaxRows];

    [Header("Card")]
    public TextMeshProUGUI titleLabel;
    public TextMeshProUGUI spentLabel;
    public TextMeshProUGUI emptyLabel;
    public TextMeshProUGUI statusLabel;
    public Button closeButton;

    [Header("Daylight v2: arm, then confirm")]
    [Tooltip("Shown in place of Unlock once it has been pressed: Cancel and \"Yes, −5\".")]
    public Button[] rowCancels = new Button[MaxRows];
    public Button[] rowConfirms = new Button[MaxRows];
    public TextMeshProUGUI[] rowConfirmLabels = new TextMeshProUGUI[MaxRows];

    [Tooltip("The sentence above the hints: what they cost and when.")]
    public TextMeshProUGUI introLabel;

    int _armed = -1;

    BuildingData _marker;
    bool _busy;

    void Awake()
    {
        for (int i = 0; i < rowButtons.Length; i++)
        {
            if (rowButtons[i] == null)
                continue;

            // Captured per iteration, or every row opens the last hint.
            int index = i;
            rowButtons[i].onClick.AddListener(delegate { Reveal(index); });
        }

        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        for (int i = 0; i < MaxRows; i++)
        {
            int index = i;

            if (i < rowConfirms.Length && rowConfirms[i] != null)
                rowConfirms[i].onClick.AddListener(delegate { _armed = -1; Post(index); });

            if (i < rowCancels.Length && rowCancels[i] != null)
                rowCancels[i].onClick.AddListener(delegate { _armed = -1; Draw(); });
        }
    }

    void OnEnable()
    {
        AppStrings.OnLanguageChanged += Draw;
        Draw();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= Draw;
    }

    /// <summary>How many hints this marker has, for the button that opens this.</summary>
    public static int CountFor(BuildingData marker)
    {
        return marker != null && marker.hints != null ? marker.hints.Length : 0;
    }

    public void Open(BuildingData marker)
    {
        _marker = marker;
        _busy = false;
        _armed = -1;

        Say(null);
        gameObject.SetActive(true);
        Draw();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    void Draw()
    {
        if (titleLabel != null)
            titleLabel.text = AppStrings.Get("hint.title");

        MarkerHint[] hints = _marker != null ? _marker.hints : null;
        int count = hints != null ? hints.Length : 0;

        if (emptyLabel != null)
        {
            emptyLabel.gameObject.SetActive(count == 0);
            emptyLabel.text = AppStrings.Get("hint.none");
        }

        if (introLabel != null)
        {
            bool firstFree = count > 0 && hints[0] != null && hints[0].cost <= 0;
            introLabel.gameObject.SetActive(count > 0);
            introLabel.text = AppStrings.Get(firstFree ? "hint.introFree" : "hint.intro");
        }

        int spent = 0;
        bool blocked = false;

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] == null)
                continue;

            bool used = i < count && hints[i] != null;
            rows[i].gameObject.SetActive(used);

            if (!used)
                continue;

            MarkerHint hint = hints[i];
            if (hint.revealed)
                spent += hint.cost;

            if (rowTitles[i] != null)
                rowTitles[i].text = string.Format(AppStrings.Get("hint.nth"), i + 1);

            bool openable = !hint.revealed && !blocked;
            bool armed = openable && _armed == i;

            // The right-hand side says one thing: what it cost, the button that
            // opens it, the confirmation, or that the one before comes first.
            bool note = hint.revealed || !openable;

            if (rowChipHolders[i] != null)
                rowChipHolders[i].SetActive(note);

            if (rowChips[i] != null)
            {
                rowChips[i].text = hint.revealed
                    ? (hint.cost > 0 ? string.Format(AppStrings.Get("hint.cost"), hint.cost)
                                     : AppStrings.Get("hint.free"))
                    : AppStrings.Get("hint.previous");
            }

            if (rowBodies[i] != null)
            {
                bool shown = hint.revealed && hint.HasText;
                rowBodies[i].gameObject.SetActive(shown);
                rowBodies[i].text = shown ? hint.body.Text : string.Empty;
                rowBodies[i].color = ThemeManager.Get(ThemeRole.TextPrimary);
            }

            if (rowButtons[i] != null)
            {
                rowButtons[i].gameObject.SetActive(openable && !armed);
                rowButtons[i].interactable = !_busy;
            }

            if (rowActions[i] != null)
            {
                rowActions[i].text = hint.cost > 0
                    ? string.Format(AppStrings.Get("hint.unlockCost"), hint.cost)
                    : AppStrings.Get("hint.show");
            }

            if (i < rowCancels.Length && rowCancels[i] != null)
                rowCancels[i].gameObject.SetActive(armed);

            if (i < rowConfirms.Length && rowConfirms[i] != null)
            {
                rowConfirms[i].gameObject.SetActive(armed);
                rowConfirms[i].interactable = !_busy;
            }

            if (i < rowConfirmLabels.Length && rowConfirmLabels[i] != null)
                rowConfirmLabels[i].text = string.Format(AppStrings.Get("hint.yes"), hint.cost);

            if (!hint.revealed)
                blocked = true;
        }

        if (spentLabel != null)
        {
            spentLabel.gameObject.SetActive(spent > 0);
            if (spent > 0)
                spentLabel.text = string.Format(AppStrings.Get("hint.spent"), spent);
        }
    }

    /// <summary>
    /// The first press. A free hint opens; one that costs asks first, in its own
    /// row - "Cancel" and "Yes, −5" - because the points go whether or not the
    /// hint helps.
    /// </summary>
    void Reveal(int index)
    {
        MarkerHint[] costs = _marker != null ? _marker.hints : null;

        if (!_busy && costs != null && index >= 0 && index < costs.Length && costs[index] != null &&
            costs[index].cost > 0 && _armed != index)
        {
            _armed = index;
            Haptics.Tick();
            Draw();
            return;
        }

        Post(index);
    }

    void Post(int index)
    {
        MarkerHint[] hints = _marker != null ? _marker.hints : null;

        if (_busy || hints == null || index < 0 || index >= hints.Length)
            return;

        MarkerHint hint = hints[index];

        if (hint == null || hint.revealed)
            return;

        _busy = true;
        Say("hint.opening", ThemeRole.TextMuted);
        Draw();

        StartCoroutine(Post(hint));
    }

    IEnumerator Post(MarkerHint hint)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            Failed();
            yield break;
        }

        string body = "{\"p_hint\":\"" + Escape(hint.id) + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/reveal_hint"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[Hint] reveal_hint failed (" + request.responseCode + "): " +
                                 (request.downloadHandler != null ? request.downloadHandler.text : ""));

                // A phone asked for a hint on a competitive trail without an
                // account. The server answered "a competitive trail needs a real
                // account" and the sheet said "That did not unlock", which is
                // true, useless, and reads as a fault of ours.
                Failed(Postgrest.InWords(Postgrest.Said(request)));
                yield break;
            }

            Reply reply = Read(request.downloadHandler.text);

            if (reply != null && reply.body != null)
            {
                // Written onto the marker rather than kept here, so the sheet
                // behind this card knows too and reopening does not ask again.
                hint.body = reply.body;
                hint.revealed = true;
            }
        }

        _busy = false;
        Say(null);
        Draw();
    }

    void Failed(string said = null)
    {
        _busy = false;

        if (!string.IsNullOrWhiteSpace(said))
            Tell(said, ThemeRole.StatusPoor);
        else
            Say("hint.failed", ThemeRole.StatusPoor);

        Draw();
    }

    /// <summary>The server's own words, which are not a key to look up.</summary>
    void Tell(string sentence, ThemeRole role)
    {
        if (statusLabel == null)
            return;

        statusLabel.text = sentence;
        statusLabel.color = ThemeManager.Get(role);
        statusLabel.gameObject.SetActive(true);
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

    static Reply Read(string json)
    {
        try
        {
            return JsonUtility.FromJson<Reply>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Hint] Unreadable reply: " + exception.Message);
            return null;
        }
    }

    [Serializable]
    class Reply
    {
        public LocalisedText body;
        public int cost;
        public int points_left;
    }

    static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
