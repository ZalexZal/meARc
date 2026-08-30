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

    [Serializable]
    public class Trail
    {
        public string id;
        public string name;
        public string role;
    }

    public List<Trail> Mine { get; private set; } = new List<Trail>();

    public string SelectedId { get; private set; }

    public bool HasLoaded { get; private set; }

    /// <summary>What went wrong on the last call, for a screen to show. Null when fine.</summary>
    public string LastError { get; private set; }

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
        SelectedId = PlayerPrefs.GetString(SelectedKey, string.Empty);
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
        StartCoroutine(RefreshMine());
    }

    public void Select(string worldId)
    {
        if (SelectedId == worldId)
            return;

        SelectedId = worldId;
        PlayerPrefs.SetString(SelectedKey, worldId ?? string.Empty);
        PlayerPrefs.Save();

        Raise();
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

        LastError = null;

        string url = config.Endpoint(
            "/rest/v1/world_members?select=role,worlds(id,name)");

        using (UnityWebRequest request = SupabaseAuth.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastError = request.error;
                Debug.LogWarning("[Trails] Could not read membership: " + request.error);
                Raise();
                yield break;
            }

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
                role = rows[i].role
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
    [Serializable] class WorldRow { public string id; public string name; }
}
