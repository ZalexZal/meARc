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

    /// <summary>
    /// Waits until there is somebody to make a request as.
    ///
    /// HasSettledYet answers true when there is no auth object at all, which is
    /// right for a caller deciding what to draw and wrong for one about to call
    /// the server: at launch the object exists a frame or two later than the
    /// screens do, so "no instance yet" read as "ready" and the first request
    /// went out with no token. A phone logged my_team_detail failed (401) one
    /// second after launch, concluded the walker was on no team, and asked them
    /// to make one they already had.
    ///
    /// Bounded, because a screen that waits for ever on a service that never
    /// arrives is worse than one that tries and fails.
    /// </summary>
    public static IEnumerator Ready(float seconds = 8f)
    {
        float waited = 0f;

        while (waited < seconds &&
               (Instance == null || !Instance.HasSettled))
        {
            waited += Time.deltaTime;
            yield return null;
        }
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
                             string work, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || !IsSignedIn)
        {
            Fail(done, "auth.noSession");
            yield break;
        }

        // Required, and checked before anything goes out: the form says so too,
        // but an account made without it would be asked again at every launch.
        if (!LineOfWork.IsValid(work))
        {
            Fail(done, "err.work");
            yield break;
        }

        _working = true;

        // The refresh below makes this a permanent account, which would send
        // the profile off to be read for its line of work - and it would come
        // back empty, because the PATCH that fills it in has not run yet, and
        // the question would pop up over the sign-up that just answered it.
        _workFor = UserId;

        // The name and the line of work go into user_metadata as well as into
        // profiles. GoTrue keeps the metadata, but the trigger that copies it
        // into profiles runs on INSERT only, and this is an update of the guest
        // row - so profiles is written separately below. Keeping both means a
        // rebuilt profile still knows what to call somebody and what they do.
        string body = SignUpBody(email, password, displayName, work);

        bool held = false;

        // redirect_to decides where the confirmation link lands: the website's
        // page that says "confirmed, go back to the app". Ignored while email
        // confirmation is off, because then no link is sent.
        string url = config.Endpoint("/auth/v1/user?redirect_to=" +
                                     UnityWebRequest.EscapeURL(SiteLink.Confirmed()));

        using (UnityWebRequest request = Put(url, body))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                _working = false;
                Fail(done, Explain(request), request);
                yield break;
            }

            // With email confirmation on, the address is not the account's yet:
            // the server sends a link, parks the address as new_email, and the
            // account stays anonymous until the link is opened. Reporting that
            // as success is how somebody ends up "signed up" with nothing to
            // sign in with.
            UserReply reply = null;
            try { reply = JsonUtility.FromJson<UserReply>(request.downloadHandler.text); }
            catch (Exception) { /* an unreadable reply is treated as the old shape */ }

            held = reply != null && !string.IsNullOrEmpty(reply.new_email) &&
                   string.IsNullOrEmpty(reply.email);
        }

        // Same uid, now permanent - or still anonymous, if held above.
        yield return Refresh();

        if (!string.IsNullOrWhiteSpace(displayName))
            yield return SetDisplayName(displayName, null);

        // Not allowed to fail the sign-up: the account exists either way. If it
        // does not land, the question is asked once at the next launch instead.
        yield return SetWork(work, null);

        _working = false;

        if (held)
        {
            PendingEmail = email;
            if (done != null)
                done("account.checkEmail");
            yield break;
        }

        PendingEmail = null;

        if (done != null)
            done(null);
    }

    /// <summary>
    /// An address waiting on its confirmation link, or null. Set by SignUp when
    /// the server holds the account back; cleared once it is no longer anonymous.
    /// </summary>
    public string PendingEmail { get; private set; }

    [Serializable]
    class UserReply
    {
        public string email;
        public string new_email;
    }

    /// <summary>
    /// After the link has been opened: asks for a fresh token, which says
    /// whether the account is permanent now. "account.notYet" if it is not.
    /// </summary>
    public IEnumerator CheckConfirmed(Action<string> done)
    {
        _working = true;
        yield return Refresh();
        _working = false;

        if (IsSignedIn && !IsAnonymous)
        {
            PendingEmail = null;
            if (done != null) done(null);
        }
        else if (done != null)
        {
            done("account.notYet");
        }
    }

    /// <summary>
    /// Sends the confirmation link again. An account made from a guest is an
    /// email change as far as the server is concerned, so that is the kind of
    /// link asked for; a brand-new sign-up would be "signup".
    /// </summary>
    public IEnumerator ResendConfirmation(Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || string.IsNullOrEmpty(PendingEmail))
        {
            Fail(done, "auth.noSession");
            yield break;
        }

        string body = "{\"type\":\"email_change\",\"email\":\"" + Escape(PendingEmail) + "\"}";

        using (UnityWebRequest request =
               Post(config.Endpoint("/auth/v1/resend?redirect_to=" +
                                    UnityWebRequest.EscapeURL(SiteLink.Confirmed())), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(done, Explain(request), request);
                yield break;
            }
        }

        if (done != null)
            done("account.resent");
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

    // ------------------------------------------------------------ line of work

    /// <summary>
    /// What the account does, as profiles.work holds it: null until it has been
    /// read, and null for an account that has never answered.
    /// </summary>
    public string Work { get; private set; }

    /// <summary>Raised when the line of work has been read or changed.</summary>
    public static event Action OnWorkChanged;

    /// <summary>
    /// Raised once per account per launch, when the profile was read and says
    /// this account has never answered. Not raised when the read failed.
    /// </summary>
    public static event Action OnWorkMissing;

    /// <summary>The account whose profile has been read (or is being), this launch.</summary>
    string _workFor;

    /// <summary>
    /// Reads profiles.work once for each permanent account this launch. A guest
    /// is never asked; nor is anybody whose profile could not be read.
    /// </summary>
    void ReadWorkIfNew()
    {
        if (UserId == _workFor)
            return;

        _workFor = UserId;
        Work = null;

        if (OnWorkChanged != null)
            OnWorkChanged();

        if (!IsAnonymous && !string.IsNullOrEmpty(UserId))
            StartCoroutine(ReadWork(UserId, true));
    }

    IEnumerator ReadWork(string uid, bool mayRetry)
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        if (config == null)
            yield break;

        string url = config.Endpoint("/rest/v1/profiles?id=eq." + uid + "&select=work");
        string reply = null;
        bool ok;
        bool retry;

        using (UnityWebRequest request = Get(url))
        {
            yield return request.SendWebRequest();

            ok = request.result == UnityWebRequest.Result.Success;
            retry = mayRetry && Postgrest.WorthRetrying(request);

            if (ok)
                reply = request.downloadHandler.text;
            else if (!retry)
                Debug.LogWarning("[Auth] profile read failed (" + request.responseCode + "): " +
                                 Postgrest.Said(request));
        }

        // A token a moment too new, as ProfileService meets it at launch.
        if (retry)
        {
            yield return new WaitForSeconds(2f);
            yield return ReadWork(uid, false);
            yield break;
        }

        // Somebody else by now, or a read that came back with nothing usable:
        // either way, nobody is asked anything on the strength of it.
        if (!ok || uid != UserId || IsAnonymous)
            yield break;

        string work;
        if (!LineOfWork.TryRead(reply, out work))
            yield break;

        Work = work;

        if (OnWorkChanged != null)
            OnWorkChanged();

        if (work == null && OnWorkMissing != null)
            OnWorkMissing();
    }

    /// <summary>Writes the line of work onto the reader's own profile row.</summary>
    public IEnumerator SetWork(string work, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (!LineOfWork.IsValid(work))
        {
            Fail(done, "err.work");
            yield break;
        }

        if (config == null || !IsSignedIn || string.IsNullOrEmpty(UserId))
        {
            Fail(done, "auth.noSession");
            yield break;
        }

        string url = config.Endpoint("/rest/v1/profiles?id=eq." + UserId);

        using (UnityWebRequest request = Patch(url, WorkPatchBody(work)))
        {
            // The row back, not 204: a PATCH that RLS filtered down to nothing
            // also answers 204, and Settings said "Saved." over it.
            request.SetRequestHeader("Prefer", "return=representation");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success ||
                !LineOfWork.PatchLanded(request.downloadHandler.text))
            {
                Fail(done, "work.saveFailed", request);
                yield break;
            }
        }

        Work = work;

        if (OnWorkChanged != null)
            OnWorkChanged();

        if (done != null)
            done(null);
    }

    /// <summary>
    /// The body of the sign-up PUT. The line of work is required and has to be
    /// one of the four: anything else is refused here rather than sent, so a
    /// value can never carry extra JSON in with it.
    /// </summary>
    public static string SignUpBody(string email, string password, string displayName, string work)
    {
        if (!LineOfWork.IsValid(work))
            throw new ArgumentException("Not a line of work: " + work, "work");

        string data = string.IsNullOrWhiteSpace(displayName)
            ? string.Empty
            : "\"display_name\":\"" + Escape(displayName.Trim()) + "\",";

        return "{\"email\":\"" + Escape(email) + "\"," +
               "\"password\":\"" + Escape(password) + "\"," +
               "\"data\":{" + data + "\"work\":\"" + work + "\"}}";
    }

    /// <summary>The body of PATCH profiles for the line of work.</summary>
    public static string WorkPatchBody(string work)
    {
        if (!LineOfWork.IsValid(work))
            throw new ArgumentException("Not a line of work: " + work, "work");

        return "{\"work\":\"" + work + "\"}";
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
    /// <summary>
    /// Moves the account to another address.
    ///
    /// With email confirmation on, nothing changes until the new address is
    /// confirmed, and the reply says so by carrying new_email - reported back as
    /// "account.emailSent" so the screen does not claim a change that has not
    /// happened. Without it the address changes at once.
    /// </summary>
    public IEnumerator ChangeEmail(string email, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || !IsSignedIn || IsAnonymous)
        {
            Fail(done, "auth.noSession");
            yield break;
        }

        _working = true;

        using (UnityWebRequest request = Put(config.Endpoint("/auth/v1/user"),
                                             "{\"email\":\"" + Escape(email.Trim()) + "\"}"))
        {
            yield return request.SendWebRequest();
            _working = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(done, Explain(request), request);
                yield break;
            }

            string reply = request.downloadHandler != null ? request.downloadHandler.text : "";

            if (reply.Contains("\"new_email\":\""))
            {
                if (done != null)
                    done("account.emailSent");

                yield break;
            }
        }

        yield return Refresh();

        if (done != null)
            done(null);
    }

    /// <summary>
    /// A new password, after proving the current one.
    ///
    /// The check first: a phone left unlocked on a desk should not be enough to
    /// take an account over. Signing in with the current password is the check,
    /// and it also hands back a fresh session for the change itself.
    /// </summary>
    public IEnumerator ChangePassword(string current, string next, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || !IsSignedIn || IsAnonymous || string.IsNullOrEmpty(Email))
        {
            Fail(done, "auth.noSession");
            yield break;
        }

        if (string.IsNullOrEmpty(next) || next.Length < 8)
        {
            Fail(done, "account.pwShort");
            yield break;
        }

        _working = true;

        string proof = "{\"email\":\"" + Escape(Email) + "\"," +
                       "\"password\":\"" + Escape(current) + "\"}";

        using (UnityWebRequest request =
               Post(config.Endpoint("/auth/v1/token?grant_type=password"), proof, config.anonKey))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                _working = false;
                Fail(done, "account.pwWrong", request);
                yield break;
            }

            Handle(request, "password check");
        }

        using (UnityWebRequest request = Put(config.Endpoint("/auth/v1/user"),
                                             "{\"password\":\"" + Escape(next) + "\"}"))
        {
            yield return request.SendWebRequest();
            _working = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Fail(done, Explain(request), request);
                yield break;
            }
        }

        if (done != null)
            done(null);
    }

    /// <summary>
    /// Deletes the account for good, then carries on as a fresh guest.
    ///
    /// Somebody who made trails is sent to the website instead
    /// ("account.deleteOnWebsite"): only there can they choose whether their
    /// trails stay for other people, and only there are the uploaded photos
    /// removed with them. Everybody else - nearly every walker - is two taps.
    /// </summary>
    public IEnumerator DeleteAccount(Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || !IsSignedIn)
        {
            Fail(done, "auth.noSession");
            yield break;
        }

        _working = true;

        using (UnityWebRequest request =
               Post(config.Endpoint("/rest/v1/rpc/what_delete_removes"), "{}", AccessToken))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                _working = false;
                Fail(done, "account.deleteFailed", request);
                yield break;
            }

            string reply = request.downloadHandler != null ? request.downloadHandler.text : "";

            // An empty list prints as "trails_owned": [] - anything else means
            // there is at least one trail to decide about.
            string compact = reply.Replace(" ", "");
            bool ownsTrails = compact.Contains("\"trails_owned\":[{");

            if (ownsTrails)
            {
                _working = false;

                if (done != null)
                    done("account.deleteOnWebsite");

                yield break;
            }
        }

        using (UnityWebRequest request =
               Post(config.Endpoint("/rest/v1/rpc/delete_my_account"),
                    "{\"p_keep_trails\":false}", AccessToken))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                _working = false;
                Fail(done, "account.deleteFailed", request);
                yield break;
            }
        }

        _working = false;

        // The account is gone; its tokens are worthless. Straight to a new guest.
        yield return SignOut(null);

        if (done != null)
            done(null);
    }

    public IEnumerator SignOut(Action done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config != null && IsSignedIn)
        {
            _working = true;

            using (UnityWebRequest request =
                   Post(config.Endpoint("/auth/v1/logout?scope=local"), "{}", AccessToken))
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

    /// <summary>
    /// A string made safe to sit between quotes in JSON: backslash, quote, and
    /// every control character. A pasted password with a newline in it used to
    /// make the body unreadable and the sign-up fail with a parse error.
    /// </summary>
    static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        StringBuilder sb = new StringBuilder(value.Length + 8);

        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ')
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else
                        sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
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

        ReadWorkIfNew();
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
