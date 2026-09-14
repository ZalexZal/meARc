using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Your numbers and your badges, as the server works them out.
///
/// Nothing here decides a badge. my_profile() computes every one from what the
/// database holds, so a phone cannot award itself anything; this class only
/// fetches the answer and, separately, tells the server when a stop has been
/// reached - the one fact only the phone can know.
///
/// Quiet when the server has not been updated yet: my_profile arrives with
/// migration 0038, and until it has been run the You tab simply shows no numbers
/// rather than an error.
/// </summary>
public class ProfileService : MonoBehaviour
{
    public static ProfileService Instance { get; private set; }

    /// <summary>Raised when a fresh profile has arrived, or been cleared.</summary>
    public static event Action OnChanged;

    /// <summary>The last profile the server sent, or null.</summary>
    public ProfileData Latest { get; private set; }

    /// <summary>False when the server does not have my_profile yet.</summary>
    public bool Available { get; private set; } = true;

    bool _loading;

    void Awake()
    {
        Instance = this;
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
        // A different person may be signed in now; their numbers are not these.
        Latest = null;
        Raise();
        Load();
    }

    public void Load()
    {
        if (!_loading)
            StartCoroutine(Fetch());
    }

    IEnumerator Fetch()
    {
        _loading = true;

        yield return SupabaseAuth.Ready();

        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || SupabaseAuth.Instance == null || !SupabaseAuth.Instance.IsSignedIn)
        {
            _loading = false;
            yield break;
        }

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/my_profile"), "{}", null))
        {
            yield return request.SendWebRequest();

            // A token a moment too new. Wait for the clocks to agree and ask once
            // more, rather than leaving the tab blank until the app restarts.
            if (Postgrest.WorthRetrying(request))
            {
                yield return new WaitForSeconds(2f);
                _loading = false;
                StartCoroutine(Fetch());
                yield break;
            }

            _loading = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                // 404: the function does not exist yet. Not an error worth a
                // message on screen - the tab just has no numbers until it does.
                Available = request.responseCode != 404;
                Debug.LogWarning("[Profile] my_profile failed (" + request.responseCode + "): " +
                                 Postgrest.Said(request));
                yield break;
            }

            Available = true;

            try
            {
                Latest = JsonUtility.FromJson<ProfileData>(request.downloadHandler.text);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Profile] Unreadable profile: " + exception.Message);
                Latest = null;
            }
        }

        Raise();
    }

    /// <summary>
    /// Tells the server a stop was reached. "camera" when its marker was tapped,
    /// "map" when AR could not calibrate and standing inside its radius counted.
    /// Fire and forget: a visit that fails to record costs a badge's progress,
    /// never the walk.
    /// </summary>
    public void RecordVisit(string markerId, bool throughCamera)
    {
        if (string.IsNullOrEmpty(markerId))
            return;

        StartCoroutine(PostVisit(markerId, throughCamera ? "camera" : "map"));
    }

    IEnumerator PostVisit(string markerId, string via)
    {
        yield return SupabaseAuth.Ready();

        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null)
            yield break;

        string body = "{\"p_marker\":\"" + markerId.Replace("\"", "") +
                      "\",\"p_via\":\"" + via + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/record_visit"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning("[Profile] record_visit failed (" + request.responseCode + "): " +
                                 Postgrest.Said(request));
                yield break;
            }
        }

        // The numbers on the You tab just changed; ask again next time it opens
        // rather than now, which would be a request per stop for a screen that
        // is not on show.
        Latest = null;
    }

    static void Raise()
    {
        if (OnChanged != null)
            OnChanged();
    }

    // ---------------------------------------------------------------- shapes

    [Serializable]
    public class ProfileData
    {
        public string name;
        public string since;
        public int trails_walked;
        public int stops_found;
        public long points;
        public TrailRow[] trails;
        public BadgeRow[] badges;

        public int Earned
        {
            get
            {
                int n = 0;
                if (badges != null)
                    for (int i = 0; i < badges.Length; i++)
                        if (badges[i] != null && badges[i].earned)
                            n++;
                return n;
            }
        }
    }

    [Serializable]
    public class TrailRow
    {
        public string id;
        public string name;
        public string mode;
        public string team;
        public int done;
        public int total;
        public long points;
        public bool finished;
        public bool ended;
    }

    [Serializable]
    public class BadgeRow
    {
        public string key;
        public bool earned;
        public int progress;
        public int target;
        public int tier;
    }
}
