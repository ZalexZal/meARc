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

        [Tooltip("education | tourism | estate | other. Blank when unknown.")]
        public string category;

        [Tooltip("Never null in practice; key is empty when the trail has no city.")]
        public CityJson city;

        /// <summary>The city in the app language, or "" when there is none.</summary>
        public string CityName() { return CityJson.NameOf(city); }
    }

    /// <summary>
    /// The city a trail is in, as the database stores it: {key, bg, en, lat, lng}.
    /// JsonUtility cannot tell a missing or null object from an empty one, so
    /// callers test for an empty key (or just use CityName()).
    /// </summary>
    [Serializable]
    public class CityJson
    {
        public string key;
        public string bg;
        public string en;
        public double lat;
        public double lng;

        public static string NameOf(CityJson city)
        {
            if (city == null || string.IsNullOrEmpty(city.key))
                return string.Empty;

            bool bulgarian = AppStrings.CurrentLanguage == AppLanguage.Bulgarian;
            string first = bulgarian ? city.bg : city.en;
            string second = bulgarian ? city.en : city.bg;

            return !string.IsNullOrEmpty(first) ? first
                 : !string.IsNullOrEmpty(second) ? second
                 : string.Empty;
        }
    }

    /// <summary>
    /// The label for a category key, in the app language. Blank for an unknown
    /// or empty key, so an old row shows nothing rather than "cat.".
    /// </summary>
    public static string CategoryLabel(string category)
    {
        if (string.IsNullOrEmpty(category))
            return string.Empty;

        string key = "cat." + category;
        string label = AppStrings.Get(key);
        return label == key ? string.Empty : label;
    }

    /// <summary>
    /// "Tourism · Yambol", "Tourism" or "" - whichever parts exist. The line
    /// under a trail's name in the pickers.
    /// </summary>
    public static string CategoryAndCity(string category, CityJson city)
    {
        string label = CategoryLabel(category);
        string name = CityJson.NameOf(city);

        if (label.Length == 0) return name;
        return name.Length == 0 ? label : label + " · " + name;
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

        // Whether it has started (0058), asked with the summary.
        yield return FetchStart(worldId);

        if (done != null) done();
    }

    /// <summary>trail_start (0058): whether, and when, a live event begins.</summary>
    [Serializable]
    public class StartInfo
    {
        public string start_mode;
        public string starts_at;
        public bool started;
        public bool ended;

        /// <summary>A trail that waits for its start and has not had it.</summary>
        public bool Waiting { get { return start_mode != null && start_mode != "none" && !started; } }
    }

    /// <summary>The selected trail's start, or null until asked.</summary>
    public StartInfo SelectedStart { get; private set; }

    Coroutine _startWatch;

    IEnumerator FetchStart(string worldId)
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null)
            yield break;

        string body = "{\"p_world\":\"" + worldId.Replace("\"", "") + "\"}";
        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/trail_start"), body, null))
        {
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success || worldId != SelectedId)
                yield break;

            bool wasWaiting = SelectedStart != null && SelectedStart.Waiting;
            try { SelectedStart = JsonUtility.FromJson<StartInfo>(request.downloadHandler.text); }
            catch (Exception exception) { Debug.LogWarning("[Trails] Unreadable start: " + exception.Message); yield break; }

            // The whistle has just gone: the stops exist now, so ask for them at
            // once rather than after 300 m of walking.
            if (wasWaiting && SelectedStart != null && !SelectedStart.Waiting)
            {
                RemoteMarkerSource source = FindFirstObjectByType<RemoteMarkerSource>();
                if (source != null)
                    source.Refresh();
            }
        }

        // Waiting: ask again every 20 seconds until it starts.
        if (SelectedStart != null && SelectedStart.Waiting && _startWatch == null)
            _startWatch = StartCoroutine(WatchStart(worldId));

        Raise();
    }

    IEnumerator WatchStart(string worldId)
    {
        while (SelectedId == worldId && SelectedStart != null && SelectedStart.Waiting)
        {
            yield return new WaitForSeconds(20f);
            yield return FetchStart(worldId);
        }
        _startWatch = null;
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
        SelectedStart = null;
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
            "/rest/v1/world_members?select=role,worlds(id,name,mode,category,city)" + mine);

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
                mode = rows[i].worlds.mode,
                category = rows[i].worlds.category,
                city = rows[i].worlds.city
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

                // The consent trigger refuses a 13-year-old nobody has agreed for
                // yet. That is a 403 and not a fault of the network.
                bool needsParent = request.responseCode == 403 &&
                    request.downloadHandler != null && request.downloadHandler.text != null &&
                    request.downloadHandler.text.Contains("parent needs to agree");

                if (done != null)
                    done(false, needsParent ? "parent" : unknownCode ? "unknown" : request.error);

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

    /// <summary>
    /// LAB: the trail code behind a team code, or null. Lets one code box take
    /// either kind of code (lab_trail_code_for_team, migration 0062).
    /// </summary>
    public IEnumerator TrailCodeForTeam(string code, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null || SupabaseAuth.Instance == null || !SupabaseAuth.Instance.IsSignedIn)
        {
            if (done != null) done(null);
            yield break;
        }

        string body = "{\"p_code\":\"" + (code ?? string.Empty).Trim().Replace("\"", string.Empty) + "\"}";
        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/lab_trail_code_for_team"), body, null))
        {
            yield return request.SendWebRequest();

            string found = null;
            if (request.result == UnityWebRequest.Result.Success && request.downloadHandler != null)
            {
                string text = request.downloadHandler.text.Trim();
                if (text != "null" && text.Length > 2)
                    found = text.Trim('"');
            }

            if (done != null) done(found);
        }
    }

    void Raise()
    {
        if (OnTrailsChanged != null)
            OnTrailsChanged();
    }

    // ----------------------------------------------------------- the library

    /// <summary>A public trail near the reader, as trails_near returns it.</summary>
    [Serializable]
    public class Listed
    {
        public string id;
        public string name;
        public string mode;
        public int stop_count;
        public double distance_m;
        public string category;
        public CityJson city;

        [Tooltip("Where it starts: lat/lng from trails_near, start_lat/start_lng from public_trails.")]
        public double lat;
        public double lng;
        public double start_lat;
        public double start_lng;

        /// <summary>The city in the app language, or "" when there is none.</summary>
        public string CityName() { return CityJson.NameOf(city); }
    }

    [Serializable] class ListedList { public Listed[] items; }

    /// <summary>Every public trail (public_trails, newest first, up to 100), for the
    /// search. Distance is not filled in here: it depends on where the reader is
    /// when they look, so the screen measures it from start_lat/start_lng.</summary>
    public List<Listed> All { get; private set; } = new List<Listed>();

    /// <summary>False until public_trails has answered once.</summary>
    public bool AllLoaded { get; private set; }

    /// <summary>True when the last public_trails call failed.</summary>
    public bool AllFailed { get; private set; }

    public IEnumerator LoadAll(Action done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            // No server to ask: say so rather than wait forever.
            AllFailed = true;
            if (done != null) done();
            yield break;
        }

        yield return SupabaseAuth.Ready();

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/public_trails"), "{}", null))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    Listed[] rows = JsonUtility.FromJson<ListedList>(
                        "{\"items\":" + request.downloadHandler.text + "}").items;

                    All.Clear();
                    if (rows != null)
                        All.AddRange(rows);

                    AllLoaded = true;
                    AllFailed = false;
                }
                catch (Exception exception)
                {
                    AllFailed = true;
                    Debug.LogWarning("[Trails] Unreadable public trails: " + exception.Message);
                }
            }
            else
            {
                AllFailed = true;
                Debug.LogWarning("[Trails] public_trails failed (" + request.responseCode + ")");
            }
        }

        if (done != null) done();
    }

    /// <summary>Public trails near the last position asked about, nearest first,
    /// minus the ones already joined.</summary>
    public List<Listed> Near { get; private set; } = new List<Listed>();

    /// <summary>False until an answer came back, so "none near you" is never
    /// said while the question is still out.</summary>
    public bool NearLoaded { get; private set; }

    /// <summary>How many public trails nearby were left out because they are already joined.</summary>
    public int NearJoined { get; private set; }

    public IEnumerator LoadNear(double latitude, double longitude, Action done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            if (done != null) done();
            yield break;
        }

        yield return SupabaseAuth.Ready();

        string body = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{{\"p_lat\":{0},\"p_lng\":{1},\"p_radius_m\":50000}}", latitude, longitude);

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/trails_near"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    Listed[] rows = JsonUtility.FromJson<ListedList>(
                        "{\"items\":" + request.downloadHandler.text + "}").items;

                    Near.Clear();
                    NearJoined = 0;

                    for (int i = 0; rows != null && i < rows.Length; i++)
                    {
                        bool joined = false;

                        for (int j = 0; j < Mine.Count; j++)
                            joined |= Mine[j].id == rows[i].id;

                        if (!joined)
                            Near.Add(rows[i]);
                        else
                            NearJoined++;
                    }

                    NearLoaded = true;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Trails] Unreadable library: " + exception.Message);
                }
            }
            else
            {
                Debug.LogWarning("[Trails] trails_near failed (" + request.responseCode + ")");
            }
        }

        if (done != null) done();
    }

    /// <summary>
    /// Joins a public trail without a code. join_listed refuses anything that is
    /// not listed and approved, so this cannot open a private trail by its id.
    /// </summary>
    public IEnumerator JoinListed(string worldId, Action<bool> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || SupabaseAuth.Instance == null || !SupabaseAuth.Instance.IsSignedIn)
        {
            if (done != null) done(false);
            yield break;
        }

        string body = "{\"p_world\":\"" + (worldId ?? string.Empty).Replace("\"", "") + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/join_listed"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[Trails] join_listed failed (" + request.responseCode + ")");
                if (done != null) done(false);
                yield break;
            }
        }

        yield return RefreshMine();
        Select(worldId);

        for (int i = Near.Count - 1; i >= 0; i--)
        {
            if (Near[i].id == worldId)
                Near.RemoveAt(i);
        }

        if (done != null) done(true);
    }

    // ----------------------------------------------------------- the preview

    /// <summary>
    /// What the trail preview shows before a reader joins: trail_summary, and
    /// the stops as markers_near hands them to anybody (published stops of an
    /// open trail; a one-at-a-time trail gives a stranger stop one only).
    /// </summary>
    public class TrailPreview
    {
        public string id;
        public string name;
        public string mode;
        public string category;
        public CityJson city;
        public int stopCount;

        /// <summary>The stops that came back, in no particular order.</summary>
        public List<PreviewStop> stops = new List<PreviewStop>();

        /// <summary>
        /// True when every stop the summary counts came back, so a route length
        /// means something. False leaves the stops tile and hides the rest.
        /// </summary>
        public bool stopsKnown;

        /// <summary>How many of the stops ask a question.</summary>
        public int questions;

        /// <summary>False when trail_summary itself failed.</summary>
        public bool ok;
    }

    public class PreviewStop
    {
        public string id;
        public double lat;
        public double lng;
        public int step;
        public string titleBg;
        public string titleEn;

        /// <summary>Its title in the app language, the other one, or "Stop N".</summary>
        public string Title
        {
            get
            {
                bool bulgarian = AppStrings.CurrentLanguage == AppLanguage.Bulgarian;
                string first = bulgarian ? titleBg : titleEn;
                string second = bulgarian ? titleEn : titleBg;

                if (!string.IsNullOrWhiteSpace(first)) return first.Trim();
                if (!string.IsNullOrWhiteSpace(second)) return second.Trim();

                return step > 0 ? string.Format(AppStrings.Get("stop.untitledN"), step)
                                : AppStrings.Get("stop.untitled");
            }
        }
    }

    [Serializable] class PreviewSummary { public string id; public string name; public string mode; public int stop_count; public string category; public CityJson city; }
    [Serializable] class PreviewSummaryList { public PreviewSummary[] items; }
    [Serializable] class PreviewText { public string title; public string question; }
    [Serializable] class PreviewContent { public PreviewText bg; public PreviewText en; }
    [Serializable] class PreviewMarker { public string id; public double lat; public double lng; public int step; public PreviewContent content; public string status; }
    [Serializable] class PreviewMarkerList { public PreviewMarker[] items; }

    /// <summary>
    /// Asks for one trail's preview. The stops are asked for around the trail's
    /// own start (from the lists already loaded), else its city, else the
    /// reader - markers_near answers within 5 km of the point it is given.
    /// </summary>
    public IEnumerator Preview(string worldId, Action<TrailPreview> done)
    {
        TrailPreview result = new TrailPreview { id = worldId };
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || string.IsNullOrEmpty(worldId))
        {
            if (done != null) done(result);
            yield break;
        }

        yield return SupabaseAuth.Ready();

        string world = worldId.Replace("\"", "");

        using (UnityWebRequest request = SupabaseAuth.Post(
                   config.Endpoint("/rest/v1/rpc/trail_summary"), "{\"p_world\":\"" + world + "\"}", null))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    PreviewSummary[] rows = JsonUtility.FromJson<PreviewSummaryList>(
                        "{\"items\":" + request.downloadHandler.text + "}").items;

                    if (rows != null && rows.Length > 0)
                    {
                        result.name = rows[0].name;
                        result.mode = rows[0].mode;
                        result.category = rows[0].category;
                        result.city = rows[0].city;
                        result.stopCount = rows[0].stop_count;
                        result.ok = true;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Trails] Unreadable preview summary: " + exception.Message);
                }
            }
            else
            {
                Debug.LogWarning("[Trails] preview trail_summary failed (" + request.responseCode + ")");
            }
        }

        if (!result.ok)
        {
            if (done != null) done(result);
            yield break;
        }

        double lat, lng;
        if (!PreviewCentre(worldId, result.city, out lat, out lng))
        {
            if (done != null) done(result);
            yield break;
        }

        string body = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{{\"p_world_id\":\"{0}\",\"p_lat\":{1},\"p_lng\":{2},\"p_radius_m\":5000}}",
            world, lat, lng);

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/markers_near"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    PreviewMarker[] rows = JsonUtility.FromJson<PreviewMarkerList>(
                        "{\"items\":" + request.downloadHandler.text + "}").items;

                    for (int i = 0; rows != null && i < rows.Length; i++)
                    {
                        // A creator previewing their own trail is sent its drafts too.
                        if (!string.IsNullOrEmpty(rows[i].status) && rows[i].status != "published")
                            continue;

                        PreviewContent content = rows[i].content;
                        bool asks = content != null &&
                                    ((content.bg != null && !string.IsNullOrWhiteSpace(content.bg.question)) ||
                                     (content.en != null && !string.IsNullOrWhiteSpace(content.en.question)));
                        if (asks)
                            result.questions++;

                        result.stops.Add(new PreviewStop
                        {
                            id = rows[i].id,
                            lat = rows[i].lat,
                            lng = rows[i].lng,
                            step = rows[i].step,
                            titleBg = content != null && content.bg != null ? content.bg.title : null,
                            titleEn = content != null && content.en != null ? content.en.title : null
                        });
                    }

                    result.stopsKnown = result.stops.Count > 0 && result.stops.Count >= result.stopCount;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Trails] Unreadable preview stops: " + exception.Message);
                }
            }
            else
            {
                Debug.LogWarning("[Trails] preview markers_near failed (" + request.responseCode + ")");
            }
        }

        if (done != null) done(result);
    }

    bool PreviewCentre(string worldId, CityJson city, out double lat, out double lng)
    {
        lat = lng = 0d;

        foreach (List<Listed> list in new[] { All, Near })
        {
            for (int i = 0; list != null && i < list.Count; i++)
            {
                if (list[i].id != worldId)
                    continue;

                if (list[i].start_lat != 0d || list[i].start_lng != 0d)
                {
                    lat = list[i].start_lat;
                    lng = list[i].start_lng;
                    return true;
                }

                if (list[i].lat != 0d || list[i].lng != 0d)
                {
                    lat = list[i].lat;
                    lng = list[i].lng;
                    return true;
                }
            }
        }

        if (city != null && (city.lat != 0d || city.lng != 0d))
        {
            lat = city.lat;
            lng = city.lng;
            return true;
        }

        UserLocationService location = UserLocationService.Instance;
        if (location != null && location.HasValidPose)
        {
            lat = location.Latitude;
            lng = location.Longitude;
            return true;
        }

        return false;
    }

    [Serializable] class MembershipList { public MembershipRow[] items; }
    [Serializable] class MembershipRow { public string role; public WorldRow worlds; }
    [Serializable] class WorldRow { public string id; public string name; public string mode; public string category; public CityJson city; }
}
