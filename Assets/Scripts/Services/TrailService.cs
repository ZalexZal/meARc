using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Which trails this reader belongs to, and which one they are looking at.
///
/// Phase 0 had one world id compiled into the app. Membership makes that wrong:
/// a reader can hold several trails and has to be able to move between them, so
/// the chosen one lives here and everything that fetches markers asks.
///
/// The list comes from world_members, which row-level security already limits to
/// the caller's own rows - so this asks for everything and is answered with only
/// what belongs to them. No filtering happens on the device, because filtering on
/// the device is not security.
/// </summary>
public class TrailService : MonoBehaviour
{
    const string SelectedKey = "selected_world";

    public static TrailService Instance { get; private set; }

    /// <summary>Raised when the list changes or a different trail is chosen.</summary>
    public static event Action OnTrailsChanged;

    /// <summary>
    /// Whether the open trail refuses an anonymous walker.
    ///
    /// Asked of the server rather than worked out here. Today needs_account is
    /// true for ctf and false for freefall, which is not the same line as
    /// "keeps a score" - assert_may_play was written when ctf was the only game
    /// and was never widened. Wherever that line ends up, the server is the one
    /// that draws it, so this reads its answer instead of keeping a second copy
    /// of the rule that could disagree.
    /// </summary>
    public bool SelectedNeedsAccount { get; private set; }

    /// <summary>
    /// Which trail SelectedNeedsAccount was actually answered for. Empty until a
    /// summary has come back. A screen that depends on the flag compares this
    /// with SelectedId rather than trusting a false that may just mean "never
    /// asked".
    /// </summary>
    public string SummaryFor { get; private set; } = string.Empty;

    /// <summary>
    /// The open trail's mode as the server reports it, or null before it has
    /// answered.
    ///
    /// Selected resolves by finding the trail in the loaded list, and right
    /// after joining by code that list has not been fetched yet - so
    /// SelectedIsGame was false at exactly the moment the team gate asked, and
    /// the gate skipped. This comes straight from trail_summary and does not
    /// wait for the list.
    /// </summary>
    public string SelectedSummaryMode { get; private set; }

    [Serializable]
    public class Trail
    {
        public string id;
        public string name;
        public string role;

        [Tooltip("guide | freefall | ctf | educational. Blank when the row predates " +
                 "the column, which the UI treats as guide.")]
        public string mode;
    }

    /// <summary>
    /// Games keep score, walks do not. The same pair the database calls
    /// mode_is_scored, repeated here because the app has to decide what to draw
    /// before it has asked the server anything.
    /// </summary>
    public static bool IsGame(string mode)
    {
        return mode == "ctf" || mode == "freefall";
    }

    /// <summary>
    /// Ordered trails are walked marker after marker: ctf and guide. The other
    /// two are markers scattered with no sequence between them. Mirrors
    /// mode_is_ordered, which is where the rule is actually enforced.
    /// </summary>
    public static bool IsOrdered(string mode)
    {
        return string.IsNullOrEmpty(mode) || mode == "ctf" || mode == "guide";
    }

    /// <summary>Whether the trail being walked runs in order.</summary>
    public bool SelectedIsOrdered
    {
        get
        {
            Trail trail = Selected;
            return trail == null || IsOrdered(trail.mode);
        }
    }

    /// <summary>
    /// Asks the server whether this trail wants a real account, and remembers it.
    ///
    /// One call, when a trail is entered. Quiet on failure: the answer is used
    /// to warn somebody early, and a warning that could not be fetched is worth
    /// less than the walk it would interrupt.
    /// </summary>
    public void LoadNeedsAccount(string worldId, Action done = null)
    {
        if (string.IsNullOrEmpty(worldId))
        {
            if (done != null) done();
            return;
        }

        StartCoroutine(FetchNeedsAccount(worldId, done));
    }

    IEnumerator FetchNeedsAccount(string worldId, Action done)
    {
        // Same reason as the team card: this runs at launch, and a summary asked
        // for before there is a session comes back 401 and leaves the flag false.
        Debug.Log("[Trails] asking for summary of " + worldId);

        yield return SupabaseAuth.Ready();

        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            Debug.LogWarning("[Trails] no SupabaseConfig, summary not fetched");
            if (done != null) done();
            yield break;
        }

        string body = "{\"p_world\":\"" + worldId.Replace("\"", "") + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/trail_summary"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string payload = request.downloadHandler != null
                    ? request.downloadHandler.text : string.Empty;

                // A table-returning function comes back as an array, which
                // JsonUtility will not parse at the top level.
                try
                {
                    SummaryList list = JsonUtility.FromJson<SummaryList>(
                        "{\"items\":" + payload + "}");

                    bool got = list != null && list.items != null && list.items.Length > 0;

                    SelectedNeedsAccount = got && list.items[0].needs_account;
                    SelectedSummaryMode = got ? list.items[0].mode : null;
                    SummaryFor = got ? worldId : string.Empty;

                    // Two flags decide whether a walker is offered a team form
                    // they are allowed to use. When they are wrong the card
                    // either offers a button the server refuses or hides one it
                    // would have accepted, and neither leaves a trace.
                    Debug.Log("[Trails] summary " + worldId + " mode=" + SelectedSummaryMode +
                              " needsAccount=" + SelectedNeedsAccount);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Trails] Unreadable summary: " + exception.Message);
                }
            }
            else
            {
                Debug.LogWarning("[Trails] trail_summary failed (" +
                                 request.responseCode + ")");
            }
        }

        if (done != null) done();
    }

    [Serializable]
    class Summary
    {
        public bool needs_account;
        public string mode;
    }

    [Serializable]
    class SummaryList
    {
        public Summary[] items;
    }

    /// <summary>Whether the trail being walked keeps a score.</summary>
    public bool SelectedIsGame
    {
        get
        {
            Trail trail = Selected;
            return trail != null && IsGame(trail.mode);
        }
    }

    public List<Trail> Mine { get; private set; } = new List<Trail>();

    public string SelectedId { get; private set; }

    public bool HasLoaded { get; private set; }

    /// <summary>What went wrong on the last call, for a screen to show. Null when fine.</summary>
    public string LastError { get; private set; }

    bool _retriedMine;

    /// <summary>
    /// Asks for the list again if the last attempt failed. Called when My trails
    /// is opened, so "Couldn't reach the server" is a moment, not a state the
    /// app stays in until it is restarted.
    /// </summary>
    public void RetryIfFailed()
    {
        // Not while one is in flight, and not more than every few seconds: a
        // failure redraws the list, the list asks again, and without this a dead
        // network would be asked in a tight loop.
        if (string.IsNullOrEmpty(LastError) || _fetchingMine ||
            Time.unscaledTime - _lastMineAttempt < 5f)
            return;

        StartCoroutine(RefreshMine());
    }

    bool _fetchingMine;
    float _lastMineAttempt = -100f;

    public Trail Selected
    {
        get
        {
            for (int i = 0; i < Mine.Count; i++)
            {
                if (Mine[i].id == SelectedId)
                    return Mine[i];
            }

            return null;
        }
    }

    void Awake()
    {
        Instance = this;
        SelectedId = PlayerPrefs.GetString(AccountScope.Key(SelectedKey), string.Empty);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnEnable()
    {
        SupabaseAuth.OnSessionChanged += HandleSession;
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= HandleSession;
    }

    void HandleSession()
    {
        // Awake read this before there was a session, so it read the "nobody"
        // drawer and found nothing. Now that the account is known, read that
        // account's own choice. Without this line, scoping the key would lose
        // the chosen trail on every single launch.
        string was = SelectedId;
        SelectedId = PlayerPrefs.GetString(AccountScope.Key(SelectedKey), string.Empty);

        // And say so, because this is not the only thing listening to the
        // session. RemoteMarkerSource subscribes to the same event, and both
        // subscribers run in whatever order they happened to enable in. When it
        // ran first it read the empty id, fell back to its built-in world id,
        // fetched zero markers for a trail nobody is on, and never asked again:
        // a returning walker opened the app to an empty map and "Nothing within
        // 500 m" for a trail whose stop was 460 metres away.
        //
        // Only when it changed, so an ordinary token refresh does not send
        // everybody back to the server for markers they already have.
        if (SelectedId != was)
            Raise();

        StartCoroutine(RefreshMine());

        // The trail carried over from the last run never asked the server about
        // itself. Only DoorFlow.Entered loads the summary, and reopening the app
        // on a trail already chosen does not go through it - so needs_account
        // was false, the team card offered Make a team to an anonymous walker on
        // a competitive trail, and the server refused. Restarting the app is the
        // single most likely thing to happen on the day.
        if (!string.IsNullOrEmpty(SelectedId))
            LoadNeedsAccount(SelectedId);
    }

    public void Select(string worldId)
    {
        if (SelectedId == worldId)
            return;

        SelectedId = worldId;

        // Belongs to the trail that was open, not this one.
        SelectedNeedsAccount = false;
        SelectedSummaryMode = null;
        SummaryFor = string.Empty;
        PlayerPrefs.SetString(AccountScope.Key(SelectedKey), worldId ?? string.Empty);
        PlayerPrefs.Save();

        Raise();
    }

    /// <summary>
    /// Gives up a trail.
    ///
    /// Deletes this reader's own membership row and nothing else - the policy that
    /// allows it matches user_id against auth.uid(), so this cannot remove anybody
    /// else however the request is shaped. The trail and its markers are untouched;
    /// what goes is the reader's claim on them, and rejoining is the same code
    /// again.
    /// </summary>
    public IEnumerator Leave(string worldId, Action<bool> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        SupabaseAuth auth = SupabaseAuth.Instance;

        if (config == null || auth == null || !auth.IsSignedIn ||
            string.IsNullOrEmpty(worldId) || string.IsNullOrEmpty(auth.UserId))
        {
            if (done != null) done(false);
            yield break;
        }

        string url = config.Endpoint("/rest/v1/world_members" +
                                     "?world_id=eq." + UnityWebRequest.EscapeURL(worldId) +
                                     "&user_id=eq." + UnityWebRequest.EscapeURL(auth.UserId));

        bool ok;

        using (UnityWebRequest request = SupabaseAuth.Delete(url))
        {
            yield return request.SendWebRequest();

            ok = request.result == UnityWebRequest.Result.Success;

            if (!ok)
            {
                LastError = request.error;
                Debug.LogWarning("[Trails] Could not leave: " + request.error);
            }
        }

        if (ok)
        {
            // Standing in the trail they just left. Nothing is selected now, and
            // the map goes back to asking which trail - which is honest, because
            // there is no longer one to draw.
            if (SelectedId == worldId)
            {
                SelectedId = string.Empty;
                PlayerPrefs.DeleteKey(AccountScope.Key(SelectedKey));
                PlayerPrefs.Save();
            }

            yield return RefreshMine();
        }

        Raise();

        if (done != null)
            done(ok);
    }

    /// <summary>
    /// Reads the trails this reader is a member of.
    ///
    /// The embedded worlds(...) is PostgREST following the foreign key from
    /// world_members, which saves a second round trip and, more importantly,
    /// means the name and the membership can never disagree.
    /// </summary>
    public IEnumerator RefreshMine()
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null)
            yield break;

        _lastMineAttempt = Time.unscaledTime;
        _fetchingMine = true;
        try
        {
            yield return FetchMine(config);
        }
        finally
        {
            _fetchingMine = false;
        }
    }

    IEnumerator FetchMine(SupabaseConfig config)
    {
        LastError = null;

        // Asking for our own rows explicitly.
        //
        // Row-level security is what makes this safe and it is not what makes it
        // correct: an owner is allowed to read the whole roster of a trail they
        // own, so without this filter the list would show one row per member and
        // draw the same trail several times over. The policy decides what may be
        // returned; the query decides what is being asked for, and those are two
        // different questions.
        SupabaseAuth auth = SupabaseAuth.Instance;
        string mine = auth != null && !string.IsNullOrEmpty(auth.UserId)
            ? "&user_id=eq." + UnityWebRequest.EscapeURL(auth.UserId)
            : string.Empty;

        string url = config.Endpoint(
            "/rest/v1/world_members?select=role,worlds(id,name,mode)" + mine);

        using (UnityWebRequest request = SupabaseAuth.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // A 401 at launch is "JWT issued at future": the session is a
                // second old and the database's clock has not caught up with the
                // one that signed it. Not the server being unreachable. Asked once more once it is - a phone
                // logged exactly this and then sat on "Couldn't reach the server"
                // with an empty list, which empties everything that depends on
                // knowing which trail you are on, team card included.
                if (request.responseCode == 401 && !_retriedMine)
                {
                    _retriedMine = true;
                    yield return SupabaseAuth.Ready();
                    yield return FetchMine(config);
                    yield break;
                }

                LastError = request.error;
                Debug.LogWarning("[Trails] Could not read membership: " + request.error);
                Raise();
                yield break;
            }

            _retriedMine = false;

            Parse(request.downloadHandler.text);
        }

        HasLoaded = true;

        // A trail chosen on a previous run may have been left, deleted, or belong
        // to an identity this install no longer has.
        if (!string.IsNullOrEmpty(SelectedId) && Selected == null)
            Select(Mine.Count > 0 ? Mine[0].id : string.Empty);
        else if (string.IsNullOrEmpty(SelectedId) && Mine.Count == 1)
            Select(Mine[0].id);   // only one, so there is nothing to choose

        Raise();
    }

    void Parse(string json)
    {
        Mine.Clear();

        // JsonUtility cannot read a bare array, so it is wrapped. Same trick as
        // RemoteMarkerSource, for the same reason.
        MembershipRow[] rows;

        try
        {
            rows = JsonUtility.FromJson<MembershipList>("{\"items\":" + json + "}").items;
        }
        catch (Exception exception)
        {
            LastError = exception.Message;
            Debug.LogWarning("[Trails] Membership was unreadable: " + exception.Message);
            return;
        }

        if (rows == null)
            return;

        for (int i = 0; i < rows.Length; i++)
        {
            if (rows[i] == null || rows[i].worlds == null ||
                string.IsNullOrEmpty(rows[i].worlds.id))
            {
                continue;
            }

            Mine.Add(new Trail
            {
                id = rows[i].worlds.id,
                name = rows[i].worlds.name,
                role = rows[i].role,
                mode = rows[i].worlds.mode
            });
        }
    }

    /// <summary>
    /// Redeems a join code. The RPC does the work, because the caller cannot read
    /// the worlds table until the row it would be reading says they may.
    /// </summary>
    public IEnumerator Join(string code, Action<bool, string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            if (done != null) done(false, "no config");
            yield break;
        }

        if (SupabaseAuth.Instance == null || !SupabaseAuth.Instance.IsSignedIn)
        {
            if (done != null) done(false, "not signed in");
            yield break;
        }

        string trimmed = (code ?? string.Empty).Trim();
        string body = "{\"p_code\":\"" + trimmed.Replace("\"", string.Empty) + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/join_world"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // The function raises when the code is unknown, which arrives as a
                // 400. Anything else is a real fault worth showing differently.
                bool unknownCode = request.responseCode == 400 || request.responseCode == 404;

                if (done != null)
                    done(false, unknownCode ? "unknown" : request.error);

                yield break;
            }

            // The RPC returns the world id as a bare quoted string.
            string id = request.downloadHandler.text.Trim().Trim('"');

            yield return RefreshMine();
            Select(id);

            if (done != null)
                done(true, id);
        }
    }

    void Raise()
    {
        if (OnTrailsChanged != null)
            OnTrailsChanged();
    }

    [Serializable] class MembershipList { public MembershipRow[] items; }
    [Serializable] class MembershipRow { public string role; public WorldRow worlds; }
    [Serializable] class WorldRow { public string id; public string name; public string mode; }
}
