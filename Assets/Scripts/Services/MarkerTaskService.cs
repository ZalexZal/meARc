using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Asks the server whether an answer was right, and remembers what it said.
///
/// The client never decides. It cannot: the accepted answers live in a table with
/// no select policy, and the only way to reach them is check_answer, which
/// compares, records the attempt, applies whatever the creator chose to happen on
/// a wrong answer, and hands back a verdict. Everything below is bookkeeping
/// around that one call.
///
/// What is cached here is the *answer the server already gave*, not a judgement of
/// our own. It exists so a lock survives the app being closed and so the sheet can
/// draw the right state without a round trip - not so the phone can decide
/// anything. If the two ever disagree, the server wins on the next call.
///
/// Needs the Phase 2.5 migration (marker_answers, marker_progress, check_answer).
/// Until that is applied the RPC answers 404 and every check reports Unavailable,
/// which the sheet shows as "couldn't check that" rather than as a wrong answer.
/// </summary>
public class MarkerTaskService : MonoBehaviour
{
    const string ProgressKey = "marker_progress";

    public static MarkerTaskService Instance { get; private set; }

    /// <summary>Raised when any marker's state changes, so the map can grey one out.</summary>
    public static event Action OnProgressChanged;

    public enum Verdict
    {
        Correct,
        Wrong,

        /// <summary>The server could not be asked. Not a wrong answer.</summary>
        Unavailable
    }

    public class Result
    {
        public Verdict verdict;

        /// <summary>The creator's own words, when they wrote any. May be empty.</summary>
        public string feedback;

        public MarkerProgress progress;
    }

    /// <summary>
    /// What the server last said about one marker for this reader. Mirrors the
    /// marker_progress row.
    /// </summary>
    [Serializable]
    public class MarkerProgress
    {
        public string markerId;
        public int attempts;
        public bool solved;
        public bool skipped;

        /// <summary>Unix seconds. Zero when not locked.</summary>
        public long lockedUntil;

        public int points;

        /// <summary>Points lost on the last wrong answer, for the bubble to say so.</summary>
        public int lastDeduction;

        public bool IsLocked
        {
            get { return SecondsLeft > 0f; }
        }

        public float SecondsLeft
        {
            get
            {
                if (lockedUntil <= 0L)
                    return 0f;

                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                return Mathf.Max(0f, lockedUntil - now);
            }
        }

        /// <summary>True once the reader can do nothing further here.</summary>
        public bool IsFinished
        {
            get { return solved || skipped; }
        }
    }

    readonly Dictionary<string, MarkerProgress> _progress =
        new Dictionary<string, MarkerProgress>();

    void Awake()
    {
        Instance = this;
        LoadCache();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        // A different reader has a different history. Anonymous identities do not
        // survive a reinstall, which is why a CTF world should require a real
        // account - see MARKER-CONTENT.md.
        SupabaseAuth.OnSessionChanged += Forget;
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= Forget;
    }

    // ------------------------------------------------------------------- state

    /// <summary>
    /// What is known about this marker. Never null, so callers can read it without
    /// a null check - a marker nobody has answered is simply all zeroes.
    /// </summary>
    public MarkerProgress Progress(string markerId)
    {
        if (string.IsNullOrEmpty(markerId))
            return new MarkerProgress();

        if (_progress.TryGetValue(markerId, out MarkerProgress found))
            return found;

        MarkerProgress fresh = new MarkerProgress { markerId = markerId };
        _progress[markerId] = fresh;
        return fresh;
    }

    /// <summary>Total points from every marker the server has scored.</summary>
    public int TotalPoints
    {
        get
        {
            int total = 0;
            foreach (KeyValuePair<string, MarkerProgress> pair in _progress)
                total += pair.Value.points;

            return total;
        }
    }

    // ------------------------------------------------------------------ asking

    public void CheckAnswer(string markerId, string answer, Action<Result> done)
    {
        StartCoroutine(Check(markerId, answer, done));
    }

    IEnumerator Check(string markerId, string answer, Action<Result> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || string.IsNullOrEmpty(markerId))
        {
            Report(done, new Result { verdict = Verdict.Unavailable, progress = Progress(markerId) });
            yield break;
        }

        string body = "{\"p_marker\":\"" + Escape(markerId) + "\"," +
                      "\"p_answer\":\"" + Escape(answer) + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/check_answer"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // A 404 here means Phase 2.5 has not been applied yet. Either way the
                // reader is told the check did not happen - never that they were wrong.
                Debug.LogWarning("[Task] check_answer failed (" + request.responseCode + "): " +
                                 request.error);

                Report(done, new Result
                {
                    verdict = Verdict.Unavailable,
                    progress = Progress(markerId)
                });

                yield break;
            }

            Report(done, Apply(markerId, request.downloadHandler.text));
        }
    }

    /// <summary>
    /// Reads the verdict and files it. Anything unparseable is Unavailable, not
    /// Wrong - the reader should never be marked down for our JSON.
    /// </summary>
    Result Apply(string markerId, string json)
    {
        AnswerReply reply;

        try
        {
            reply = JsonUtility.FromJson<AnswerReply>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Task] Unreadable verdict: " + exception.Message);
            return new Result { verdict = Verdict.Unavailable, progress = Progress(markerId) };
        }

        if (reply == null)
            return new Result { verdict = Verdict.Unavailable, progress = Progress(markerId) };

        MarkerProgress progress = Progress(markerId);
        progress.attempts = reply.attempts;
        progress.solved = reply.solved;
        progress.skipped = reply.skipped;
        progress.points = reply.points;
        progress.lastDeduction = reply.deducted;
        progress.lockedUntil = ParseTime(reply.locked_until);

        SaveCache();

        if (OnProgressChanged != null)
            OnProgressChanged();

        return new Result
        {
            verdict = reply.correct ? Verdict.Correct : Verdict.Wrong,
            feedback = reply.feedback,
            progress = progress
        };
    }

    static void Report(Action<Result> done, Result result)
    {
        if (done != null)
            done(result);
    }

    /// <summary>
    /// ISO-8601 from Postgres to unix seconds. An unparseable timestamp becomes no
    /// lock at all, which fails towards letting the reader carry on.
    /// </summary>
    static long ParseTime(string iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
            return 0L;

        if (DateTimeOffset.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.AssumeUniversal |
                                    System.Globalization.DateTimeStyles.AdjustToUniversal,
                                    out DateTimeOffset parsed))
        {
            return parsed.ToUnixTimeSeconds();
        }

        Debug.LogWarning("[Task] Could not read locked_until: " + iso);
        return 0L;
    }

    static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Replace("\\", "\\\\").Replace("\"", "\\\"")
                   .Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
    }

    // ------------------------------------------------------------------- cache

    void Forget()
    {
        _progress.Clear();
        PlayerPrefs.DeleteKey(ProgressKey);

        if (OnProgressChanged != null)
            OnProgressChanged();
    }

    void LoadCache()
    {
        string stored = PlayerPrefs.GetString(ProgressKey, string.Empty);
        if (string.IsNullOrEmpty(stored))
            return;

        try
        {
            ProgressList list = JsonUtility.FromJson<ProgressList>(stored);
            if (list == null || list.items == null)
                return;

            for (int i = 0; i < list.items.Count; i++)
            {
                MarkerProgress item = list.items[i];
                if (item != null && !string.IsNullOrEmpty(item.markerId))
                    _progress[item.markerId] = item;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Task] Could not read stored progress: " + exception.Message);
        }
    }

    void SaveCache()
    {
        ProgressList list = new ProgressList { items = new List<MarkerProgress>(_progress.Values) };

        PlayerPrefs.SetString(ProgressKey, JsonUtility.ToJson(list));
        PlayerPrefs.Save();
    }

    [Serializable]
    class ProgressList
    {
        public List<MarkerProgress> items;
    }

    /// <summary>
    /// What check_answer returns. Named for the wire, not for us.
    /// </summary>
    [Serializable]
    class AnswerReply
    {
        public bool correct;
        public string feedback;
        public int attempts;
        public bool solved;
        public bool skipped;
        public string locked_until;
        public int points;
        public int deducted;
    }
}
