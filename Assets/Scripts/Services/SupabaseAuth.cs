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

    /// <summary>The address on the account, or null while anonymous.</summary>
    public string Email { get; private set; }

    /// <summary>
    /// What a person is called on a leaderboard.
    ///
    /// Kept here rather than read from the session, because it lives in
    /// profiles and not in the JWT: the token knows who you are, the profile
    /// knows what to call you.
    /// </summary>
    public string DisplayName { get; private set; }

    public bool IsSignedIn
    {
        get { return !string.IsNullOrEmpty(AccessToken); }
    }

    /// <summary>True once the first sign-in attempt has finished, succeeded or not.</summary>
    public bool HasSettled { get; private set; }

    /// <summary>
    /// Whether sign-in has finished, answered safely before the service exists.
    ///
    /// A caller in another object's Start cannot assume this one has run its Awake,
    /// and "no auth service at all" has to read as settled or a scene without one
    /// would hang everything waiting on it.
    /// </summary>
    public static bool HasSettledYet
    {
        get { return Instance == null || Instance.HasSettled; }
    }

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

    /// <summary>
    /// Turns the anonymous user into a permanent one, in place.
    ///
    /// PUT on the existing session, never signup. The anonymous user is a real
    /// row in auth.users with real progress hanging off its id - team
    /// memberships, solved stops, submissions - and this converts that row,
    /// keeping the uid, so there is nothing to migrate.
    ///
    /// Calling signup instead would mint a second user and strand the first. It
    /// looks like it worked, because the new account is empty and empty looks
    /// clean, and the walk somebody did this morning is simply gone.
    ///
    /// The refresh afterwards is not tidying: the reply is a user, not a session,
    /// so the token still in hand says is_anonymous and every screen reading that
    /// would go on believing it.
    /// </summary>
    public IEnumerator SignUp(string email, string password, string displayName,
                             Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || !IsSignedIn)
        {
            Fail(done, "auth.noSession");
            yield break;
        }

        _working = true;

        // The name goes into user_metadata as well as into profiles. GoTrue keeps
        // the metadata and the trigger that made the profile row does not run
        // again, so profiles has to be written separately below - but keeping
        // both means a rebuilt profile still knows what to call somebody.
        string named = string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : ",\"data\":{\"display_name\":\"" + Escape(displayName.Trim()) + "\"}";

        string body = "{\"email\":\"" + Escape(email) + "\"," +
                      "\"password\":\"" + Escape(password) + "\"" + named + "}";

        using (UnityWebRequest request = Put(config.Endpoint("/auth/v1/user"), body))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                _working = false;
                Fail(done, Explain(request), request);
                yield break;
            }
        }

        // Same uid, now permanent. The session catches up here.
        yield return Refresh();

        if (!string.IsNullOrWhiteSpace(displayName))
            yield return SetDisplayName(displayName, null);

        _working = false;

        if (done != null)
            done(null);
    }

    /// <summary>
    /// Writes the name onto the profile row, which is what the leaderboard
    /// actually reads.
    ///
    /// Separate from the account because they are separate rows: ensure_team
    /// takes the team's name from profiles.display_name, and a name that only
    /// ever reached user_metadata would leave every player on the board called
    /// "Player" followed by four characters of their id.
    /// </summary>
    public IEnumerator SetDisplayName(string displayName, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || !IsSignedIn || string.IsNullOrEmpty(UserId))
        {
            Fail(done, "auth.noSession");
            yield break;
        }

        string trimmed = displayName == null ? string.Empty : displayName.Trim();
        string body = "{\"display_name\":\"" + Escape(trimmed) + "\"}";
        string url = config.Endpoint("/rest/v1/profiles?id=eq." + UserId);

        using (UnityWebRequest request = Patch(url, body))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(done, Explain(request), request);
                yield break;
            }
        }

        DisplayName = trimmed;

        if (OnSessionChanged != null)
            OnSessionChanged();

        if (done != null)
            done(null);
    }

    /// <summary>A PATCH carrying the reader's own token, for their own row.</summary>
    static UnityWebRequest Patch(string url, string json)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        UnityWebRequest request = new UnityWebRequest(url, "PATCH");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Prefer", "return=minimal");
        request.timeout = 20;

        if (config != null)
            request.SetRequestHeader("apikey", config.anonKey);

        // profiles_update_own matches id against auth.uid(), so the anon key
        // would update nothing at all - correctly, and silently.
        if (Instance != null && !string.IsNullOrEmpty(Instance.AccessToken))
            request.SetRequestHeader("Authorization", "Bearer " + Instance.AccessToken);

        return request;
    }

    /// <summary>
    /// Signs in to an account that already exists, replacing this session.
    ///
    /// Anything done anonymously on this device stays with the anonymous user and
    /// is NOT merged into the account. Merging has no correct answer - two start
    /// times, two teams, the same stop solved twice - so the caller is expected to
    /// have said so before getting here.
    /// </summary>
    public IEnumerator SignIn(string email, string password, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
        {
            Fail(done, "auth.noSession");
            yield break;
        }

        _working = true;

        string body = "{\"email\":\"" + Escape(email) + "\"," +
                      "\"password\":\"" + Escape(password) + "\"}";

        using (UnityWebRequest request =
               Post(config.Endpoint("/auth/v1/token?grant_type=password"), body, config.anonKey))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                _working = false;
                Fail(done, Explain(request), request);
                yield break;
            }

            Handle(request, "sign-in");
        }

        _working = false;

        if (done != null)
            done(null);
    }

    /// <summary>
    /// Leaves the account and goes back to being nobody in particular.
    ///
    /// A new anonymous user follows immediately, because every screen in this app
    /// assumes there is a session - a signed-out state with no uid would show a
    /// map with no trails and no way to say why.
    /// </summary>
    public IEnumerator SignOut(Action done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config != null && IsSignedIn)
        {
            _working = true;

            using (UnityWebRequest request =
                   Post(config.Endpoint("/auth/v1/logout"), "{}", AccessToken))
            {
                yield return request.SendWebRequest();
                // A failed logout is not worth blocking on. The token is being
                // thrown away here either way.
            }

            _working = false;
        }

        AccessToken = null;
        UserId = null;
        Email = null;
        IsAnonymous = true;
        PlayerPrefs.DeleteKey(RefreshKey);
        PlayerPrefs.Save();

        yield return SignInAnonymously();

        if (done != null)
            done();
    }

    /// <summary>A PUT carrying the reader's own token. Used only by SignUp.</summary>
    static UnityWebRequest Put(string url, string json)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        UnityWebRequest request = new UnityWebRequest(url, "PUT");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.timeout = 20;

        if (config != null)
            request.SetRequestHeader("apikey", config.anonKey);

        // The anon key is no use: this changes the calling user's own row, so the
        // call has to be made as them.
        if (Instance != null && !string.IsNullOrEmpty(Instance.AccessToken))
            request.SetRequestHeader("Authorization", "Bearer " + Instance.AccessToken);

        return request;
    }

    /// <summary>
    /// GoTrue's own words where it wrote any, and a string key where it did not.
    ///
    /// "That address is already registered" and "Password should be at least 6
    /// characters" are both things a person can act on; "sign-up failed" is not.
    /// </summary>
    static string Explain(UnityWebRequest request)
    {
        string payload = request.downloadHandler != null ? request.downloadHandler.text : null;

        if (!string.IsNullOrWhiteSpace(payload))
        {
            try
            {
                AuthError error = JsonUtility.FromJson<AuthError>(payload);

                if (error != null && !string.IsNullOrWhiteSpace(error.Best))
                    return error.Best;
            }
            catch (Exception)
            {
                // Not JSON, or not a shape we know. The key below is the answer.
            }
        }

        return "auth.failed";
    }

    static void Fail(Action<string> done, string message, UnityWebRequest request = null)
    {
        if (request != null)
        {
            Debug.LogWarning("[Auth] " + message + " (" + request.responseCode + ") " +
                             Trim(request.downloadHandler != null
                                  ? request.downloadHandler.text : string.Empty));
        }

        if (done != null)
            done(message);
    }

    static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
        Email = session.user != null ? session.user.email : null;
        DisplayName = session.user != null && session.user.user_metadata != null
            ? session.user.user_metadata.display_name
            : null;

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

    /// <summary>
    /// A DELETE carrying the reader's own token, for the one thing they are
    /// allowed to delete: their membership of a trail.
    /// </summary>
    public static UnityWebRequest Delete(string url)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        UnityWebRequest request = UnityWebRequest.Delete(url);
        request.timeout = 20;

        if (config != null)
            request.SetRequestHeader("apikey", config.anonKey);

        // The anon key is no use here - row-level security matches user_id against
        // auth.uid(), and the anon key has none. A signed-out caller deletes
        // nothing, which is correct, so this does not fall back to it.
        string token = Instance != null && Instance.IsSignedIn ? Instance.AccessToken : null;

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
        public string email;
        public UserMetadata user_metadata;
    }

    [Serializable]
    class UserMetadata
    {
        public string display_name;
    }

    /// <summary>
    /// GoTrue's error shapes. It uses two, depending on which part of it failed,
    /// and a person reading "sign-up failed" learns nothing - the message it
    /// sends says whether the address is taken or the password is too short.
    /// </summary>
    [Serializable]
    class AuthError
    {
        public string msg;
        public string message;
        public string error_description;

        public string Best
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(msg)) return msg;
                if (!string.IsNullOrWhiteSpace(message)) return message;
                return error_description;
            }
        }
    }
}
