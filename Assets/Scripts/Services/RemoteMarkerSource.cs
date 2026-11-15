using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Loads markers from the backend through the markers_near RPC.
///
/// Replaces RemoteBuildingSource for the meARc platform. The load behaviour is
/// deliberately identical, because it was already right: show the cached copy
/// immediately so the app is usable in the first second, then fetch in the
/// background and replace it. A refresh that fails is silent - the reader keeps
/// whatever was already there. Only a first run with no cache and no network
/// surfaces an error.
///
/// The one rule that must not be relaxed: this never selects from the markers
/// table. It calls the RPC. In v1 the function returns everything published
/// inside the radius, but CTF mode needs markers that stay hidden until they are
/// unlocked, and a client that reads the table holds every answer in memory
/// before the player has walked anywhere. Keeping the seam here means that is
/// later a change to one function body in Postgres and nothing at all here.
/// </summary>
public class RemoteMarkerSource : MonoBehaviour
{
    [Header("Backend")]
    [Tooltip("Supabase project URL, no trailing slash. Project Settings > API > Project URL.")]
    public string supabaseUrl = "https://tajbebyhmvppqlswyjiq.supabase.co";

    [Tooltip("The anon (public) key. Project Settings > API. This one is meant to ship " +
             "inside the client - it identifies the project and nothing more. Row Level " +
             "Security is what protects the data. Never put the service_role key here.")]
    [TextArea(2, 4)]
    public string anonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InRhamJlYnlobXZwcHFsc3d5amlxIiwicm9sZSI6ImFub24iLCJpYXQiOjE3ODc3NDg0OTQsImV4cCI6MjEwMzMyNDQ5NH0.e2-l3SbkBIRBCEOfbNrvQC0i2M_FbQxZ5Lwyg3zOHLo";

    [Tooltip("Fallback only. The trail actually shown comes from TrailService, " +
             "which knows which ones this reader has joined. This is what gets " +
             "used before anyone has joined anything.")]
    public string worldId = "c1bd0005-be21-409f-9e28-40faf3abb661";

    /// <summary>
    /// The trail to fetch: whichever one the reader has open, or the built-in one
    /// while they have none.
    /// </summary>
    string ActiveWorld
    {
        get
        {
            TrailService trails = TrailService.Instance;

            if (trails != null && !string.IsNullOrEmpty(trails.SelectedId))
                return trails.SelectedId;

            return worldId;
        }
    }

    [Header("Query")]
    [Tooltip("How far around the phone to ask for. MarkerSpawner still decides what to " +
             "actually spawn, so this can be comfortably wider than its spawn radius.")]
    public int radiusMetres = 1000;

    [Header("Behaviour")]
    [Tooltip("Seconds before a fetch is abandoned and the cache is kept.")]
    public int timeoutSeconds = 20;

    [Tooltip("Refetch when the app returns from the background, so a marker published " +
             "while the phone was pocketed appears without a restart.")]
    public bool refreshOnResume = true;

    [Tooltip("Ignore a resume refresh if the last one was this recent.")]
    public float minSecondsBetweenFetches = 30f;

    [Tooltip("Also refetch as the reader walks, so markers beyond the original radius " +
             "arrive without a restart. Zero disables it.")]
    public float refetchAfterMovingMetres = 300f;

    [Header("Diagnostics")]
    public bool verboseLogging;

    const string CacheFileName = "markers.json";

    float _lastFetchTime = -999f;
    bool _fetching;

    /// <summary>
    /// A refresh that arrived while a fetch was already under way.
    ///
    /// Refresh used to return when a fetch was in flight, and that dropped the
    /// one change that matters most. On a fresh start the session lands with no
    /// trail chosen, so the first fetch goes to the fallback world; the trail
    /// list arrives a moment later, the only trail is selected, and its refresh
    /// hit the in-flight fetch and vanished. HandleTrailChanged had already
    /// recorded the new world as fetched, so nothing ever asked again, and a
    /// walker standing at a stop saw "Nothing within 500 m".
    /// </summary>
    bool _again;
    double _lastFetchLat, _lastFetchLng;
    bool _hasFetchedOnce;

    UserLocationService _location;

    string CachePath
    {
        get { return Path.Combine(Application.persistentDataPath, CacheFileName); }
    }

    void Awake()
    {
        // Instance is set in the service's own Awake, so fall back to a scene
        // search for the case where script execution order puts us first.
        _location = UserLocationService.Instance;
        if (_location == null)
            _location = FindAnyObjectByType<UserLocationService>();
    }

    void Start()
    {
        BuildingDatabase.SetLoading();
        LoadFromCache();
        StartCoroutine(FetchWhenLocated());
    }

    void OnEnable()
    {
        AppLifecycleWatcher.OnResumed += HandleResumed;
        TrailService.OnTrailsChanged += HandleTrailChanged;
        SupabaseAuth.OnSessionChanged += HandleTrailChanged;
    }

    void OnDisable()
    {
        AppLifecycleWatcher.OnResumed -= HandleResumed;
        TrailService.OnTrailsChanged -= HandleTrailChanged;
        SupabaseAuth.OnSessionChanged -= HandleTrailChanged;
    }

    string _fetchedWorld;
    string _fetchedAs;

    /// <summary>
    /// A different trail, or a different reader, means the markers on screen
    /// belong to neither. Refetched at once rather than on the usual throttle.
    ///
    /// Who asked matters as much as what was asked for. Joining the built-in trail
    /// does not change the world id at all - the fallback is that same world - so a
    /// guard that compared only the id sat still through the one event it existed
    /// to catch, and the empty list fetched before the reader was a member stayed
    /// on screen until the app was reinstalled.
    /// </summary>
    void HandleTrailChanged()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;
        string asWhom = auth != null && auth.IsSignedIn ? auth.UserId : null;

        if (_fetchedWorld == ActiveWorld && _fetchedAs == asWhom)
            return;

        _fetchedWorld = ActiveWorld;
        _fetchedAs = asWhom;

        BuildingDatabase.SetBuildings(new System.Collections.Generic.List<BuildingData>());
        Refresh();
    }

    void HandleResumed(float secondsBackgrounded)
    {
        if (!refreshOnResume)
            return;

        if (Time.unscaledTime - _lastFetchTime < minSecondsBetweenFetches)
            return;

        StartCoroutine(FetchWhenLocated());
    }

    /// <summary>
    /// Fetches again now, ignoring the throttle.
    ///
    /// The throttle exists to stop the app hammering the server on its own; a
    /// reader who pressed Retry has asked, and answering "not yet" to a button
    /// press is how a retry button earns a reputation for doing nothing.
    /// </summary>
    public void Refresh()
    {
        if (_fetching)
        {
            _again = true;
            return;
        }

        _lastFetchTime = -999f;
        BuildingDatabase.SetLoading();
        StartCoroutine(FetchWhenLocated());
    }

    void Update()
    {
        // Walking out of the fetched radius should bring in the next markers without
        // the reader doing anything. Cheap check - the location service only moves
        // once a second anyway.
        if (!_hasFetchedOnce || _fetching || refetchAfterMovingMetres <= 0f)
            return;

        if (Time.unscaledTime - _lastFetchTime < minSecondsBetweenFetches)
            return;

        if (!TryGetPosition(out double lat, out double lng))
            return;

        if (GeoMath.DistanceMetres(_lastFetchLat, _lastFetchLng, lat, lng) < refetchAfterMovingMetres)
            return;

        StartCoroutine(Fetch(lat, lng));
    }

    // -------------------------------------------------------------------- load

    void LoadFromCache()
    {
        if (!File.Exists(CachePath))
            return;

        try
        {
            Publish(File.ReadAllText(CachePath), "cache");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("Couldn't read the cached marker list: " + exception.Message);
        }
    }

    /// <summary>
    /// The RPC is radius-based, so there is nothing to ask for until the phone knows
    /// where it is. Waits rather than firing off a request from (0,0).
    /// </summary>
    IEnumerator FetchWhenLocated()
    {
        float waitedUntil = Time.unscaledTime + 30f;

        // Sign-in first. A request sent before the session exists falls back to the
        // anon key, and the anon key is a member of nothing - so it comes back empty
        // and looks exactly like a trail with no markers near you. Bounded, because
        // a reader with no network should still get the cache and a failure they can
        // retry rather than a screen that waits forever.
        while (!SupabaseAuth.HasSettledYet)
        {
            if (Time.unscaledTime > waitedUntil)
            {
                Debug.LogWarning("[Markers] Sign-in did not settle in 30s - " +
                                 "fetching anyway, which will only see open trails.");
                break;
            }

            yield return new WaitForSeconds(0.25f);
        }

        while (!TryGetPosition(out double _, out double __))
        {
            if (Time.unscaledTime > waitedUntil)
            {
                Debug.LogWarning("[Markers] No position after 30s - skipping this fetch.");
                if (!_hasFetchedOnce)
                    BuildingDatabase.SetFailed();
                yield break;
            }

            yield return new WaitForSeconds(0.5f);
        }

        TryGetPosition(out double lat, out double lng);
        yield return Fetch(lat, lng);
    }

    IEnumerator Fetch(double latitude, double longitude)
    {
        if (_fetching)
        {
            _again = true;
            yield break;
        }

        SupabaseConfig config = SupabaseConfig.Instance;

        string endpoint = config != null ? config.url : supabaseUrl;
        string world = ActiveWorld;

        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(world))
        {
            Debug.LogError("RemoteMarkerSource: no Supabase URL or no trail selected.");
            BuildingDatabase.SetFailed();
            yield break;
        }

        _fetching = true;
        _lastFetchTime = Time.unscaledTime;

        string url = endpoint.TrimEnd('/') + "/rest/v1/rpc/markers_near";

        // Invariant culture on the way out as well as the way in. On a phone set to
        // Bulgarian, string interpolation of a double writes "42,4842", which is two
        // JSON values and a syntax error rather than one coordinate.
        string body = string.Format(CultureInfo.InvariantCulture,
            "{{\"p_world_id\":\"{0}\",\"p_lat\":{1},\"p_lng\":{2},\"p_radius_m\":{3}}}",
            world, latitude, longitude, radiusMetres);

        // Sent as the reader, not as the project. After Phase 1 the bearer is what
        // decides which markers come back at all: a member sees their trail, and
        // everyone else sees only what is published in a world that is not private.
        using (UnityWebRequest request = SupabaseAuth.Post(url, body, null))
        {
            request.timeout = timeoutSeconds;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Not an error the reader needs to see if they already have markers.
                Debug.LogWarning("Marker fetch failed: " + request.error + " / " +
                                 request.downloadHandler.text);
                if (!_hasFetchedOnce)
                    BuildingDatabase.SetFailed();
                Settled(world);
                yield break;
            }

            string json = request.downloadHandler.text;

            // Says what was actually asked, and as whom.
            //
            // An empty array is a successful answer, so nothing here used to log at
            // all - and markers_near returns an empty array both when nothing is
            // nearby and when the caller is not a member of the trail. It does that
            // on purpose: an error would tell an attacker the world exists. The
            // cost is that the app cannot tell "nothing near you" from "you are not
            // in this trail", and says the friendlier of the two either way.
            SupabaseAuth auth = SupabaseAuth.Instance;
            bool asMember = auth != null && auth.IsSignedIn;
            int returned = json != null ? json.Split('"' + "id" + '"').Length - 1 : 0;

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[Markers] world={0} at {1:F5},{2:F5} r={3} as={4} -> {5} row(s)",
                world, latitude, longitude, radiusMetres,
                asMember ? "member" : "ANON KEY", returned));

            if (Publish(json, "network"))
            {
                _hasFetchedOnce = true;
                _lastFetchLat = latitude;
                _lastFetchLng = longitude;

                try
                {
                    File.WriteAllText(CachePath, json);
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning("Couldn't cache the marker list: " + exception.Message);
                }
            }
        }

        Settled(world);
    }

    /// <summary>
    /// A fetch is over. Ask again if anything was waiting on it, or if the trail
    /// it fetched is no longer the one that is open - which covers the change
    /// however it arrived, rather than only the paths somebody remembered.
    /// </summary>
    void Settled(string fetchedWorld)
    {
        _fetching = false;

        if (_again || fetchedWorld != ActiveWorld)
        {
            _again = false;
            _fetchedWorld = ActiveWorld;
            Refresh();
        }
    }

    // ----------------------------------------------------------------- publish

    /// <summary>
    /// Parses the RPC payload and hands the result to the database.
    /// </summary>
    bool Publish(string json, string origin)
    {
        // The old source checked that a published sheet hadn't started returning an
        // HTML error page with a 200. An API behind a captive portal or a proxy does
        // exactly the same thing, so the check earns its keep here too: verify the
        // shape, not the status code.
        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning($"[Markers] Empty response from {origin}.");
            return false;
        }

        string trimmed = json.TrimStart();

        if (trimmed.StartsWith("<"))
        {
            Debug.LogWarning($"[Markers] Response from {origin} was HTML, not JSON. " +
                             "Captive portal, or a wrong URL?");
            return false;
        }

        if (!trimmed.StartsWith("["))
        {
            // PostgREST reports errors as a JSON object rather than an array.
            Debug.LogWarning($"[Markers] Response from {origin} wasn't a marker array: " +
                             trimmed.Substring(0, Mathf.Min(200, trimmed.Length)));
            return false;
        }

        List<MarkerRow> rows;

        try
        {
            // JsonUtility can't parse a bare top-level array, so it is wrapped.
            rows = JsonUtility.FromJson<MarkerRowList>("{\"items\":" + json + "}").items;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[Markers] Couldn't parse {origin} payload: " + exception.Message);
            return false;
        }

        if (rows == null)
            rows = new List<MarkerRow>();

        List<BuildingData> markers = new List<BuildingData>();

        foreach (MarkerRow row in rows)
        {
            BuildingData marker = BuildRow(row);
            if (marker != null)
                markers.Add(marker);
        }

        if (verboseLogging)
            Debug.Log($"[Markers] Loaded {markers.Count} from {origin}.");

        BuildingDatabase.SetBuildings(markers);
        return true;
    }

    /// <summary>
    /// Turns one RPC row into a BuildingData. Created at runtime rather than loaded
    /// as an asset, which is the whole reason every existing screen - the spawner,
    /// the info panel, the tap chain - keeps working without a single change.
    ///
    /// Phase 0 maps title and body onto the existing name and description fields.
    /// Phase 3 replaces this with a content-jsonb-driven info panel and renames the
    /// type to MarkerData.
    /// </summary>
    BuildingData BuildRow(MarkerRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.id))
            return null;

        BuildingData marker = ScriptableObject.CreateInstance<BuildingData>();

        // The uuid is the stable id. Discovery progress is keyed on it, so it must
        // be the database's id and never anything derived from the text.
        marker.name = row.id;
        marker.buildingId = row.id;

        // Everything that arrives is meant to be seen by whoever asked: the server
        // sends drafts only to the trail's owner and editors (0054), who placed
        // them. So it is all "published" to this phone, and a draft says so.
        marker.isPublished = true;
        marker.isDraft = row.status == "draft";

        marker.latitude = row.lat;
        marker.longitude = row.lng;

        marker.markerStyle = row.style_key;
        marker.markerColourHex = row.colour_hex;
        marker.step = row.step;
        marker.radiusMetres = row.radius_m;

        // content is jsonb keyed by language. JsonUtility cannot describe a map with
        // arbitrary keys, so the two known languages are read as named fields and
        // anything else falls back. Phase 3 swaps this for a real JSON reader that
        // honours worlds.default_language for languages neither of us predicted.
        LocalisedContent en = row.content != null ? row.content.en : null;
        LocalisedContent bg = row.content != null ? row.content.bg : null;

        marker.buildingName = en != null ? en.title : null;
        marker.buildingNameBg = bg != null ? bg.title : null;
        marker.description = en != null ? en.body : null;
        marker.descriptionBg = bg != null ? bg.body : null;
        marker.externalLinkURL = en != null ? en.link : null;
        marker.externalLinkURLBg = bg != null ? bg.link : null;
        marker.signText = en != null ? en.sign : null;
        marker.signTextBg = bg != null ? bg.sign : null;

        // Only whether there is a question - the editor writes its words into
        // content and removes them with the question. The question itself still
        // comes from MarkerDetailService, below.
        marker.hasQuestion = (en != null && !string.IsNullOrWhiteSpace(en.question)) ||
                             (bg != null && !string.IsNullOrWhiteSpace(bg.question));

        // Media, hints and the question are NOT read from content any more.
        //
        // They were, and nothing ever wrote them there: the website puts uploads
        // in marker_media, questions in marker_answers and hints in marker_hints,
        // so every marker arrived here with both fields null and the sheet showed
        // a stop with nothing on it. MarkerDetailService fetches them from those
        // tables when the stop is opened.
        //
        // Left out rather than kept as a fallback. Two sources for one field is
        // the thing being fixed, and a marker whose media rows were deleted would
        // otherwise go on showing whatever content still claimed.

        // A marker with text in only one language should still be visible in the
        // other, rather than rendering as a blank row.
        if (string.IsNullOrWhiteSpace(marker.buildingName))
            marker.buildingName = marker.buildingNameBg;

        return marker;
    }

    // ---------------------------------------------------------------- position

    bool TryGetPosition(out double latitude, out double longitude)
    {
        latitude = 0d;
        longitude = 0d;

        if (_location == null)
            _location = UserLocationService.Instance;

        if (_location == null)
            return false;

        // HasValidPose covers both the Geospatial pose and the GPS fallback, which
        // is what we want: an approximate position is still good enough to ask the
        // server what is within a kilometre.
        if (!_location.HasValidPose)
            return false;

        latitude = _location.Latitude;
        longitude = _location.Longitude;
        return true;
    }

    // ------------------------------------------------------------- wire format

    [System.Serializable]
    class MarkerRowList
    {
        public List<MarkerRow> items;
    }

    [System.Serializable]
    class MarkerRow
    {
        public string id;
        public double lat;
        public double lng;
        public string style_key;
        public string colour_hex;

        // Absent until the step column and the markers_near change are applied, and
        // JsonUtility leaves it at zero when it is - which is exactly the value that
        // means "this trail has no order". The client needs no migration of its own.
        public int step;

        // Absent until 0017 is applied, and zero is exactly the value that means
        // "the server did not say", which is what BuildRow falls back from.
        public int radius_m;
        public MarkerContent content;
        public int version;
        public double distance_m;

        // Absent before 0054. Empty means published, which is all it could be then.
        public string status;
    }

    [System.Serializable]
    class MarkerContent
    {
        public LocalisedContent en;
        public LocalisedContent bg;

        // Deliberately not localised into en/bg blocks: a video is one file
        // whatever language the reader has chosen, and the question carries its own
        // per-language prompt inside itself.
        public MarkerMedia[] media;
        public MarkerTask task;
    }

    [System.Serializable]
    class LocalisedContent
    {
        public string title;
        public string body;
        public string link;
        public string sign;
        public string question;
    }
}
