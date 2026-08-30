using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Signs the reader in, silently, and keeps the session alive.
///
/// Anonymous by default. Somebody who wants to look at a trail should not have to
/// invent a password first - but the database needs to know who they are, because
/// membership is what row-level security is built on. An anonymous user is a real
/// row in auth.users with a real id; it simply has no email attached.
///
/// The session survives a restart because the refresh token is kept on the
/// device. It does not survive a reinstall, and that is the honest behaviour:
/// there is nothing to prove the new install is the same person.
/// </summary>
public class SupabaseAuth : MonoBehaviour
{
    const string RefreshKey = "sb_refresh_token";

    public static SupabaseAuth Instance { get; private set; }

    /// <summary>Raised when a session starts, refreshes, or is lost.</summary>
    public static event Action OnSessionChanged;

    public string AccessToken { get; private set; }
    public string UserId { get; private set; }
    public bool IsAnonymous { get; private set; }

    public bool IsSignedIn
    {
        get { return !string.IsNullOrEmpty(AccessToken); }
    }

    /// <summary>True once the first sign-in attempt has finished, succeeded or not.</summary>
    public bool HasSettled { get; private set; }

    [Tooltip("Refreshed this many seconds before the token actually expires, so a " +
             "request is never made with one that dies in flight.")]
    public int refreshMarginSeconds = 120;

    float _expiresAt = -1f;
    bool _working;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        StartCoroutine(Begin());
    }

    void Update()
    {
        if (!IsSignedIn || _working || _expiresAt < 0f)
            return;

        if (Time.unscaledTime < _expiresAt - refreshMarginSeconds)
            return;

        StartCoroutine(Refresh());
    }

    /// <summary>Restores the stored session, or makes a new anonymous one.</summary>
    IEnumerator Begin()
    {
        string stored = PlayerPrefs.GetString(RefreshKey, string.Empty);

        if (!string.IsNullOrEmpty(stored))
        {
            yield return Refresh();

            if (IsSignedIn)
            {
                HasSettled = true;
                yield break;
            }

            // The stored token was rejected - expired, revoked, or from a project
            // that has since been reset. Start again rather than staying stuck.
            PlayerPrefs.DeleteKey(RefreshKey);
        }

        yield return SignInAnonymously();
        HasSettled = true;
    }

    public IEnumerator SignInAnonymously()
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null)
        {
            Debug.LogError("[Auth] No SupabaseConfig in the scene.");
            yield break;
        }

        _working = true;

        // An empty body on signup is what asks for an anonymous user. It needs
        // Anonymous sign-ins switched on in the dashboard; without that the
        // response is a 422 explaining exactly that.
        using (UnityWebRequest request = Post(config.Endpoint("/auth/v1/signup"), "{}", null))
        {
            yield return request.SendWebRequest();
            Handle(request, "anonymous sign-in");
        }

        _working = false;
    }

    IEnumerator Refresh()
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null)
            yield break;

        string stored = PlayerPrefs.GetString(RefreshKey, string.Empty);
        if (string.IsNullOrEmpty(stored))
            yield break;

        _working = true;

        string body = "{\"refresh_token\":\"" + stored + "\"}";
        string url = config.Endpoint("/auth/v1/token?grant_type=refresh_token");

        using (UnityWebRequest request = Post(url, body, null))
        {
            yield return request.SendWebRequest();
            Handle(request, "session refresh");
        }

        _working = false;
    }

    void Handle(UnityWebRequest request, string what)
    {
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogWarning("[Auth] " + what + " failed: " + request.error + " " +
                             Trim(request.downloadHandler.text));
            return;
        }

        Session session;

        try
        {
            session = JsonUtility.FromJson<Session>(request.downloadHandler.text);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[Auth] " + what + " returned something unreadable: " +
                             exception.Message);
            return;
        }

        if (session == null || string.IsNullOrEmpty(session.access_token))
        {
            Debug.LogWarning("[Auth] " + what + " returned no token: " +
                             Trim(request.downloadHandler.text));
            return;
        }

        AccessToken = session.access_token;
        UserId = session.user != null ? session.user.id : null;
        IsAnonymous = session.user != null && session.user.is_anonymous;

        // Supabase reports a lifetime, not a moment, so it is turned into one here
        // rather than being recomputed at every call site.
        _expiresAt = Time.unscaledTime + Mathf.Max(session.expires_in, 60);

        if (!string.IsNullOrEmpty(session.refresh_token))
        {
            PlayerPrefs.SetString(RefreshKey, session.refresh_token);
            PlayerPrefs.Save();
        }

        if (OnSessionChanged != null)
            OnSessionChanged();
    }

    /// <summary>
    /// A POST with the project key attached, and the reader's token when there is
    /// one. Everything that talks to Supabase goes through here so the headers are
    /// written once.
    /// </summary>
    public static UnityWebRequest Post(string url, string json, string bearer)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        UnityWebRequest request = new UnityWebRequest(url, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 20;

        if (config != null)
            request.SetRequestHeader("apikey", config.anonKey);

        // The bearer decides who the database thinks is calling, and therefore
        // which rows come back. Falling back to the anon key keeps reads working
        // for a published trail before anyone has signed in.
        string token = bearer;
        if (string.IsNullOrEmpty(token))
            token = Instance != null && Instance.IsSignedIn ? Instance.AccessToken : null;
        if (string.IsNullOrEmpty(token) && config != null)
            token = config.anonKey;

        if (!string.IsNullOrEmpty(token))
            request.SetRequestHeader("Authorization", "Bearer " + token);

        return request;
    }

    public static UnityWebRequest Get(string url)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = 20;

        if (config != null)
            request.SetRequestHeader("apikey", config.anonKey);

        string token = Instance != null && Instance.IsSignedIn
            ? Instance.AccessToken
            : (config != null ? config.anonKey : null);

        if (!string.IsNullOrEmpty(token))
            request.SetRequestHeader("Authorization", "Bearer " + token);

        return request;
    }

    static string Trim(string text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        return text.Length <= 300 ? text : text.Substring(0, 300);
    }

    [Serializable]
    class Session
    {
        public string access_token;
        public string refresh_token;
        public int expires_in;
        public SessionUser user;
    }

    [Serializable]
    class SessionUser
    {
        public string id;
        public bool is_anonymous;
    }
}
