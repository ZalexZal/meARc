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

        /// <summary>
        /// Too soon after the last wrong answer. Not a wrong answer either - the
        /// reader has not been marked down, they have been asked to wait.
        /// </summary>
        Wait,

        /// <summary>
        /// A free-text answer, filed for the creator to read. The reader has done
        /// everything they can do here and is neither right nor wrong yet.
        /// </summary>
        PendingReview,

        /// <summary>The server could not be asked. Not a wrong answer.</summary>
        Unavailable
    }

    public class Result
    {
        public Verdict verdict;

        /// <summary>The creator's own words, when they wrote any. May be empty.</summary>
        public string feedback;

        /// <summary>
        /// What the server said when it refused, in its own words.
        ///
        /// Set only alongside Unavailable, and only when something actually
        /// answered. A refusal a walker can act on - "make or join a team
        /// first" - reads very differently from the offline sentence that used
        /// to stand in for it.
        /// </summary>
        public string refusal;

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

        /// <summary>
        /// A free-text answer is with the creator and has no verdict yet. Separate
        /// from solved, because the reader has finished here and has not scored.
        /// </summary>
        public bool awaitingReview;

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
            get { return solved || skipped || awaitingReview; }
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
        SupabaseAuth.OnSessionChanged += SwitchWalker;
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= SwitchWalker;
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

    /// <summary>Drops what is known about one marker. The demo's replay.</summary>
    public void Forget(string markerId)
    {
        if (!string.IsNullOrEmpty(markerId) && _progress.Remove(markerId))
            SaveCache();
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

    // ------------------------------------------------------------- the record

    /// <summary>
    /// Fills the store from the server's record of what this team has solved.
    ///
    /// Until now every screen counted from DiscoveryTracker, which is this
    /// handset's own note of what it has opened. That is wrong twice on a game:
    /// a walker who reinstalls loses a walk their team still has, and two phones
    /// on one team show two different lines - one of them says a marker is done
    /// and the other offers it as next.
    ///
    /// my_progress has been on the server since 0007 and nothing had ever called
    /// it. Answers still land here through check_answer as they always did; this
    /// only fills in what happened before the app was opened.
    /// </summary>
    public void LoadProgress(string worldId, Action done = null)
    {
        if (!string.IsNullOrEmpty(worldId))
            StartCoroutine(Fetch(worldId, done));
        else if (done != null)
            done();
    }

    IEnumerator Fetch(string worldId, Action done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            if (done != null) done();
            yield break;
        }

        string body = "{\"p_world\":\"" + worldId.Replace("\"", "\\\"") + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/my_progress"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // The local record is still there and is better than nothing:
                // this adds the team's history, it does not replace the walk.
                Debug.LogWarning("[Task] my_progress failed (" + request.responseCode + "): " +
                                 request.error);

                if (done != null) done();
                yield break;
            }

            Solved[] rows = ReadRows(request.downloadHandler.text);

            if (rows != null)
            {
                HashSet<string> solvedThere = new HashSet<string>();

                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i] == null || string.IsNullOrEmpty(rows[i].marker_id))
                        continue;

                    MarkerProgress row = Progress(rows[i].marker_id);
                    row.solved = true;
                    row.points = rows[i].points;
                    solvedThere.Add(rows[i].marker_id);
                }

                // And the other way. A team's answers go with the team, so after
                // leaving one every question it had solved stayed solved on this
                // phone and the server had no record of it: no Check, no verdict,
                // nothing to press, for good. On a game the server's record is the
                // record; a guide scores nothing and keeps its answers here.
                TrailService trails = TrailService.Instance;

                if (trails != null && trails.SelectedId == worldId && trails.SelectedIsGame)
                {
                    var here = BuildingDatabase.AllBuildings;

                    for (int i = 0; i < here.Count; i++)
                    {
                        if (here[i] == null || solvedThere.Contains(here[i].buildingId) ||
                            !_progress.TryGetValue(here[i].buildingId, out MarkerProgress stale) ||
                            !stale.solved)
                        {
                            continue;
                        }

                        stale.solved = false;
                        stale.points = 0;
                    }
                }

                SaveCache();
            }
        }

        if (OnProgressChanged != null)
            OnProgressChanged();

        if (done != null)
            done();
    }

    /// <summary>JsonUtility cannot read a top-level array, so it is wrapped.</summary>
    static Solved[] ReadRows(string json)
    {
        try
        {
            return JsonUtility.FromJson<SolvedList>("{\"items\":" + json + "}").items;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Task] Unreadable progress: " + exception.Message);
            return null;
        }
    }

    [Serializable]
    class Solved
    {
        public string marker_id;
        public int points;
    }

    [Serializable]
    class SolvedList
    {
        public Solved[] items;
    }

    // ------------------------------------------------------------------ asking

    public void CheckAnswer(string markerId, string answer, Action<Result> done)
    {
        StartCoroutine(Check(markerId, answer, done));
    }

    IEnumerator Check(string markerId, string answer, Action<Result> done)
    {
        // The demo's question is judged here and never sent, so practice cannot
        // touch a score. Same verdict shape, same path through Apply.
        if (DemoTrail.Owns(markerId))
        {
            Report(done, Apply(markerId, DemoTrail.Judge(markerId, answer)));
            yield break;
        }

        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || string.IsNullOrEmpty(markerId))
        {
            Report(done, new Result { verdict = Verdict.Unavailable, progress = Progress(markerId) });
            yield break;
        }

        // p_submitted, not p_answer. The function has always been
        // check_answer(p_marker, p_submitted); PostgREST matches an RPC on its
        // argument NAMES, so this call has never found it and every answer any
        // reader has ever given came back Unavailable.
        string body = "{\"p_marker\":\"" + Escape(markerId) + "\"," +
                      "\"p_submitted\":\"" + Escape(answer) + "\"" + Where() + "}";

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

                // A refusal is not a broken connection.
                //
                // This reported everything as Unavailable, and the screen says
                // "Couldn't reach the server, so that was not sent". A phone
                // then logged 403 Forbidden on a trail whose real answer was
                // "a competitive trail needs a real account" - a sentence the
                // walker could have acted on, replaced by one that sent them to
                // check the wifi. Anything the server actually answered is
                // passed through in its own words; only a request that never
                // arrived is called unavailable.
                string said = Postgrest.Said(request);

                Report(done, new Result
                {
                    verdict = Verdict.Unavailable,
                    refusal = said,
                    progress = Progress(markerId)
                });

                yield break;
            }

            Report(done, Apply(markerId, request.downloadHandler.text));
        }
    }

    /// <summary>
    /// Where the phone is, as the extra arguments check_answer takes.
    ///
    /// On a scored trail the server refuses an answer with no position, one the
    /// phone says is simulated, one from far off the stop, and one that implies
    /// travelling faster than anybody can (0045). Guides ignore all four. Sent
    /// in the invariant culture: a Bulgarian phone writes 42,48, which is not a
    /// number in JSON. Visits send the same (ProfileService, 0057).
    /// </summary>
    public static string Where()
    {
        UserLocationService location = UserLocationService.Instance;

        if (location == null || !location.HasValidPose)
            return string.Empty;

        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            ",\"p_lat\":{0:R},\"p_lng\":{1:R},\"p_accuracy\":{2:0.#},\"p_mock\":{3}",
            location.Latitude, location.Longitude, location.HorizontalAccuracy,
            location.IsMocked ? "true" : "false");
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

        // Refused for where the phone was, not for what it said. Nothing was
        // judged or recorded, so nothing here changes; the reader is told which
        // of the four it was, in words they can act on.
        if (string.Equals(reply.verdict, "position", StringComparison.OrdinalIgnoreCase))
        {
            return new Result
            {
                verdict = Verdict.Unavailable,
                refusal = AppStrings.Get("task.position." +
                                         (string.IsNullOrEmpty(reply.problem) ? "far" : reply.problem)),
                progress = Progress(markerId)
            };
        }

        Verdict verdict = Read(reply.verdict);
        MarkerProgress progress = Progress(markerId);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        switch (verdict)
        {
            case Verdict.Correct:
                progress.solved = true;

                // Only what was awarded this time. `already` means the team had
                // solved it before and the server sent zero, so adding is right
                // in both cases and re-answering a solved stop cannot farm it.
                progress.points += reply.points;
                progress.lockedUntil = 0L;
                break;

            case Verdict.Wrong:
                progress.attempts++;
                progress.lastDeduction = reply.deducted;

                // The server decides this, not the phone. Under the skip rule it
                // has already written the row that ends the stop, so a client
                // that disagreed would only be lying to its own reader.
                if (reply.skipped)
                    progress.skipped = true;

                // No cooldown on a stop that is over - there is nothing left to
                // wait for.
                if (reply.cooldown_seconds > 0 && !reply.skipped)
                    progress.lockedUntil = now + reply.cooldown_seconds;
                break;

            case Verdict.Wait:
                // Not an attempt. The server refused to look at it, so counting
                // it would punish somebody for pressing the button twice.
                progress.lockedUntil = now + Mathf.Max(1, reply.seconds_left);
                break;

            case Verdict.PendingReview:
                progress.attempts++;
                progress.awaitingReview = true;
                break;
        }

        SaveCache();

        if (OnProgressChanged != null)
            OnProgressChanged();

        return new Result
        {
            verdict = verdict,
            feedback = reply.after != null ? reply.after.Text : null,
            progress = progress
        };
    }

    /// <summary>
    /// The verdict word, or Unavailable for anything this build does not know.
    ///
    /// Unavailable rather than Wrong on purpose: a verdict added to the server
    /// after this APK shipped must not mark a reader down for it.
    /// </summary>
    static Verdict Read(string word)
    {
        if (string.Equals(word, "correct", StringComparison.OrdinalIgnoreCase))
            return Verdict.Correct;

        if (string.Equals(word, "incorrect", StringComparison.OrdinalIgnoreCase))
            return Verdict.Wrong;

        if (string.Equals(word, "wait", StringComparison.OrdinalIgnoreCase))
            return Verdict.Wait;

        if (string.Equals(word, "pending_review", StringComparison.OrdinalIgnoreCase))
            return Verdict.PendingReview;

        Debug.LogWarning("[Task] Unknown verdict \"" + word + "\" - treated as unavailable.");
        return Verdict.Unavailable;
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

    /// <summary>
    /// A different person is holding the phone, or the same one signing back in.
    ///
    /// This used to delete the stored progress outright, which was the right
    /// thing while there was one drawer per phone: deleting was the only way to
    /// stop one reader inheriting another's answers. Now that the key carries
    /// the account, deleting would throw away the progress of the person who
    /// has just arrived. Clearing what is in memory and reading their own cache
    /// does what the delete was for, without the collateral.
    /// </summary>
    void SwitchWalker()
    {
        _progress.Clear();
        LoadCache();

        if (OnProgressChanged != null)
            OnProgressChanged();
    }

    void LoadCache()
    {
        string stored = PlayerPrefs.GetString(AccountScope.Key(ProgressKey), string.Empty);
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

        PlayerPrefs.SetString(AccountScope.Key(ProgressKey), JsonUtility.ToJson(list));
        PlayerPrefs.Save();
    }

    [Serializable]
    class ProgressList
    {
        public List<MarkerProgress> items;
    }

    /// <summary>
    /// What check_answer returns. Named for the wire, not for us.
    ///
    /// This is the shape the function actually sends, which is not the shape that
    /// used to be written here. The old one asked for a bool called `correct` and
    /// for four fields of progress the server has never tracked; a missing bool
    /// deserialises to false, so every answer would have read as wrong even once
    /// the call itself started working.
    ///
    /// Progress lives on the phone (see MarkerProgress) precisely because the
    /// server keeps none of it per person - it keeps a team's solves.
    /// </summary>
    [Serializable]
    class AnswerReply
    {
        /// <summary>correct | incorrect | wait | pending_review | position.</summary>
        public string verdict;

        /// <summary>On "position": mock | far | fast | no_position.</summary>
        public string problem;

        public int points;

        /// <summary>Set when this stop was already solved before this attempt.</summary>
        public bool already;

        /// <summary>On "wait": how long is left of the cooldown.</summary>
        public int seconds_left;

        /// <summary>On "incorrect": how long the next attempt must wait.</summary>
        public int cooldown_seconds;

        /// <summary>The creator's rule: retry | lock | deduct | skip.</summary>
        public string on_wrong;

        /// <summary>
        /// The stop is over. Under the skip rule one wrong answer ends it, and
        /// the server has already written that down - this is not a request.
        /// </summary>
        public bool skipped;

        /// <summary>What this wrong answer cost, under the deduct rule.</summary>
        public int deducted;

        /// <summary>The creator's afterword, sent only once it has been earned.</summary>
        public LocalisedText after;
    }
}
