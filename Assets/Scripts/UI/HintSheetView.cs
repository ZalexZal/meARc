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

            // Free says so rather than showing a nought, which reads as a missing
            // number rather than as something a creator chose to give away.
            if (rowChipHolders[i] != null)
                rowChipHolders[i].SetActive(!hint.revealed);

            if (rowChips[i] != null)
            {
                rowChips[i].text = hint.cost > 0
                    ? string.Format(AppStrings.Get("hint.cost"), hint.cost)
                    : AppStrings.Get("hint.free");
            }

            // Open, next in line, or waiting behind one that is still shut.
            bool openable = !hint.revealed && !blocked;

            if (rowBodies[i] != null)
            {
                rowBodies[i].text = hint.HasText
                    ? hint.body.Text
                    : AppStrings.Get(openable ? "hint.locked" : "hint.previous");

                rowBodies[i].color = ThemeManager.Get(hint.HasText
                    ? ThemeRole.TextPrimary
                    : ThemeRole.TextMuted);
            }

            if (rowButtons[i] != null)
            {
                rowButtons[i].gameObject.SetActive(!hint.revealed);
                rowButtons[i].interactable = openable && !_busy;
            }

            if (rowActions[i] != null)
                rowActions[i].text = AppStrings.Get("hint.unlock");

            // Everything after the first unopened one is behind it.
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

    void Reveal(int index)
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
