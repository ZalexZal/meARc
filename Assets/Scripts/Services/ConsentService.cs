using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Whether a parent has agreed, for somebody under 14.
///
/// The server decides and enforces (0047): until a parent agrees, joining a
/// trail, a team, an answer or a visit is refused. This only knows the state
/// so the app can show the right door instead of a refusal, and asks for the
/// parent's email when there is none.
///
/// Asking is two requests. request_parent_consent puts the address on record;
/// then Supabase Auth's own mailer sends the parent a sign-in link to the
/// website's consent page. Opening it signs the parent in, which is the proof
/// the address is theirs, and the page takes their answer.
/// </summary>
public class ConsentService : MonoBehaviour
{
    public static ConsentService Instance { get; private set; }

    public static event Action OnChanged;

    /// <summary>not_needed, needed, pending, granted - or unknown, before the server has said.</summary>
    public string State { get; private set; } = "unknown";

    /// <summary>The address the request went to, once there is one.</summary>
    public string ParentEmail { get; private set; }

    /// <summary>
    /// True while a parent still has to agree. Before the server has answered,
    /// the phone's own record of the age question stands in, so a first run
    /// offline does not wave an under-14 through to the trails.
    /// </summary>
    public bool NeedsParent
    {
        get
        {
            if (State == "needed" || State == "pending")
                return true;

            // "not_needed" can be an answer to a question asked too early: the
            // read goes out the moment the terms are accepted, while the "13"
            // is still on its way to the server, which then says no parent is
            // needed - and a 13-year-old saw every door open. The phone's own
            // answer stands until the server says "granted".
            if (TermsService.Instance == null)
                return false;

            // Before the server answers at all, the phone's last answer stands in.
            if (State == "unknown")
                return TermsService.Instance.Under14 == true;

            return State == "not_needed" && TermsService.Instance.Under14ForThisAccount == true;
        }
    }

    [Serializable]
    class Reply
    {
        public string state;
        public string parent_email;
    }

    bool _asking;

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
        SupabaseAuth.OnSessionChanged += Refresh;
        TermsService.OnAcceptedChanged += Refresh;
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= Refresh;
        TermsService.OnAcceptedChanged -= Refresh;
    }

    /// <summary>Back from the inbox, or from the parent's phone: ask again.</summary>
    void OnApplicationFocus(bool focused)
    {
        if (focused && NeedsParent)
            Refresh();
    }

    public void Refresh()
    {
        if (!_asking && isActiveAndEnabled)
            StartCoroutine(Read());
    }

    IEnumerator Read()
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        SupabaseAuth auth = SupabaseAuth.Instance;

        if (config == null || auth == null || !auth.IsSignedIn)
            yield break;

        _asking = true;

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/consent_state"), "{}", null))
        {
            yield return request.SendWebRequest();
            _asking = false;

            if (request.result != UnityWebRequest.Result.Success)
            {
                // A server that predates 0047 has no such function. Nothing to
                // wait for there, and nothing to enforce either.
                if (request.responseCode == 404)
                    Set("not_needed", null);
                yield break;
            }

            Reply reply = null;
            try { reply = JsonUtility.FromJson<Reply>(request.downloadHandler.text); }
            catch (Exception exception) { Debug.LogWarning("[Consent] Unreadable state: " + exception.Message); }

            if (reply != null && !string.IsNullOrEmpty(reply.state))
                Set(reply.state, reply.parent_email);
        }
    }

    void Set(string state, string parentEmail)
    {
        bool changed = state != State || parentEmail != ParentEmail;
        State = state;
        ParentEmail = parentEmail;

        if (changed && OnChanged != null)
            OnChanged();
    }

    /// <summary>
    /// Puts the parent's address on record and has the link sent. The callback
    /// gets null on success, or a sentence to show.
    /// </summary>
    public IEnumerator Ask(string parentEmail, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        string email = (parentEmail ?? string.Empty).Trim();

        if (config == null)
        {
            if (done != null) done(AppStrings.Get("report.offline"));
            yield break;
        }

        string language = AppStrings.CurrentLanguage == AppLanguage.English ? "en" : "bg";
        string body = "{\"p_parent_email\":\"" + Escape(email) + "\",\"p_language\":\"" + language + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/request_parent_consent"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string said = Postgrest.Said(request) ?? string.Empty;
                Debug.LogWarning("[Consent] Request refused: " + said);

                if (done != null)
                {
                    done(AppStrings.Get(said.Contains("not an email") ? "consent.badEmail"
                                        : said.Contains("not your own") ? "consent.ownEmail"
                                        : said.Contains("too many") ? "account.slowDown"
                                        : "report.failed"));
                }
                yield break;
            }
        }

        // The link, through the auth service with the public key only: the
        // child's session has no business on the parent's sign-in.
        string otp = "{\"email\":\"" + Escape(email) + "\",\"create_user\":true}";
        string url = config.Endpoint("/auth/v1/otp?redirect_to=" + UnityWebRequest.EscapeURL(SiteLink.Consent()));

        using (UnityWebRequest request = SupabaseAuth.Post(url, otp, config.anonKey))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string said = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
                Debug.LogWarning("[Consent] The link was not sent (" + request.responseCode + "): " + said);

                if (done != null)
                    done(AppStrings.Get(request.responseCode == 429 ? "account.slowDown" : "consent.notSent"));
                Refresh();
                yield break;
            }
        }

        Set("pending", email.ToLowerInvariant());
        Refresh();

        if (done != null)
            done(null);
    }

    /// <summary>
    /// A code from a parent's account on the website: the parent agreed when
    /// they made it, so this is the whole of the consent. Null on success.
    /// </summary>
    public IEnumerator Redeem(string code, Action<string> done)
    {
        SupabaseConfig config = SupabaseConfig.Instance;
        string clean = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (config == null || clean.Length < 6)
        {
            if (done != null) done(AppStrings.Get("consent.badCode"));
            yield break;
        }

        string body = "{\"p_code\":\"" + Escape(clean) + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/redeem_child_invite"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string said = Postgrest.Said(request) ?? string.Empty;
                Debug.LogWarning("[Consent] Code refused: " + said);

                if (done != null)
                {
                    done(AppStrings.Get(said.Contains("already agreed") ? "consent.alreadyAgreed"
                                        : said.Contains("does not work") ? "consent.badCode"
                                        : "report.failed"));
                }
                yield break;
            }
        }

        Set("granted", ParentEmail);
        Refresh();

        if (done != null)
            done(null);
    }

    static string Escape(string value)
    {
        return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
