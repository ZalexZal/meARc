using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Whether this reader has agreed to the current terms.
///
/// Recorded in two places on purpose. The device remembers so the screen does not
/// reappear on every launch with no signal; the database remembers so there is an
/// actual record of what was agreed and when.
///
/// The device decides whether to show the screen, because that decision has to
/// work with no signal. The server holds the record, and the two are reconciled
/// forwards only: an acceptance that could not be sent is sent as soon as there
/// is a session to send it with. Nothing here re-asks a reader who has already
/// agreed just because the network was down when they did.
///
/// Anonymous readers accept too. Somebody who looked at a trail without making an
/// account still agreed to something, and Phase 1 is where that stops being
/// hand-waved.
/// </summary>
public class TermsService : MonoBehaviour
{
    const string AcceptedKey = "terms_accepted_version";

    /// <summary>
    /// The version the server has actually been told about, which is not the same
    /// as the version the reader accepted. Accepting works offline; recording it
    /// does not.
    /// </summary>
    const string RecordedKey = "terms_recorded_version";

    /// <summary>
    /// The version accepted on this phone by anybody, unscoped. A guest made by
    /// signing out is the same person holding the same phone a second later;
    /// asking them again was the terms screen people met twice.
    /// </summary>
    const string DeviceKey = "terms_device_version";

    /// <summary>
    /// The answer to "how old are you?", asked with the terms: "under14", "14+",
    /// or absent. Kept on the phone as well as on the profile, for the same
    /// reason as the terms: the next door has to be chosen with no signal.
    /// </summary>
    const string AgeKey = "age_band";

    /// <summary>Whose answer <see cref="AgeKey"/> is. The answer is kept per phone
    /// so a guest after sign-out is not asked again; this says whether it was
    /// given for the account signed in now.</summary>
    const string AgeOwnerKey = "age_band_owner";

    /// <summary>True, false, or null when the question has not been answered here.</summary>
    public bool? Under14
    {
        get
        {
            string band = PlayerPrefs.GetString(AgeKey, string.Empty);
            return band == "under14" ? true : band == "14+" ? (bool?)false : null;
        }
    }

    /// <summary><see cref="Under14"/>, but only when it was answered for this account.</summary>
    public bool? Under14ForThisAccount
    {
        get { return PlayerPrefs.GetString(AgeOwnerKey, string.Empty) == AccountScope.Who ? Under14 : null; }
    }

    public static TermsService Instance { get; private set; }

    public static event Action OnAcceptedChanged;

    /// <summary>The version this build requires, from SupabaseConfig.</summary>
    public string RequiredVersion
    {
        get
        {
            SupabaseConfig config = SupabaseConfig.Instance;
            return config != null ? config.termsVersion : "unversioned";
        }
    }

    public bool Accepted
    {
        get { return PlayerPrefs.GetString(AccountScope.Key(AcceptedKey), string.Empty) == RequiredVersion; }
    }

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
        SupabaseAuth.OnSessionChanged += EnsureRecorded;
        EnsureRecorded();
    }

    void OnDisable()
    {
        SupabaseAuth.OnSessionChanged -= EnsureRecorded;
    }

    /// <summary>
    /// Sends an acceptance the server never heard about.
    ///
    /// This is what makes accepting offline honest. The reader agreed, the device
    /// wrote it down, and the sign-in arrived afterwards - so the moment there is
    /// a session, the record catches up. Without it a first run in a tunnel leaves
    /// a profile that has agreed to nothing.
    /// </summary>
    /// <summary>
    /// True while the server is being asked whether this account already
    /// accepted. The terms door waits for the answer rather than asking first.
    /// </summary>
    public bool Checking { get; private set; }

    string _askedFor;

    void EnsureRecorded()
    {
        if (!Accepted)
        {
            AskServer();
            return;
        }

        if (PlayerPrefs.GetString(AccountScope.Key(RecordedKey), string.Empty) == RequiredVersion)
            return;

        if (SupabaseAuth.Instance == null || !SupabaseAuth.Instance.IsSignedIn)
            return;

        StartCoroutine(Record());
    }

    /// <summary>
    /// Records the acceptance locally at once and on the server when it can.
    ///
    /// The local write does not wait for the network. Someone who has read the
    /// terms and pressed the button has accepted them; making that depend on a
    /// signal would mean showing the same screen again in a tunnel.
    /// </summary>
    /// <summary>
    /// Signing into an account that accepted on another phone, or before a
    /// reinstall. The acceptance is on the account, not the phone, so it is
    /// read back rather than asked for twice. Never on a first run: a brand-new
    /// guest has nothing on record, and the welcome screen should not wait on a
    /// request to find that out.
    /// </summary>
    public void AskServer()
    {
        SupabaseAuth auth = SupabaseAuth.Instance;

        if (auth == null || !auth.IsSignedIn || string.IsNullOrEmpty(auth.UserId) ||
            _askedFor == auth.UserId || PlayerPrefs.GetInt(DoorFlow.WelcomeSeenKey, 0) == 0)
        {
            return;
        }

        _askedFor = auth.UserId;

        if (auth.IsAnonymous && PlayerPrefs.GetString(DeviceKey, string.Empty) == RequiredVersion &&
            Under14.HasValue)
        {
            Accept(Under14.Value);
            return;
        }

        StartCoroutine(ReadRecord(auth.UserId));
    }

    IEnumerator ReadRecord(string userId)
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null)
            yield break;

        Checking = true;

        string url = config.Endpoint("/rest/v1/profiles?select=terms_version,age_band&id=eq." +
                                     UnityWebRequest.EscapeURL(userId));

        using (UnityWebRequest request = SupabaseAuth.Get(url))
        {
            yield return request.SendWebRequest();

            bool same = SupabaseAuth.Instance != null && SupabaseAuth.Instance.UserId == userId;
            string wanted = "\"terms_version\":\"" + RequiredVersion + "\"";

            // An account that answered the age question elsewhere has answered it.
            // Without the answer the terms are not accepted here either, so the
            // door asks both, once.
            string text = request.result == UnityWebRequest.Result.Success
                ? request.downloadHandler.text : string.Empty;
            string band = text.Contains("\"age_band\":\"under14\"") ? "under14"
                        : text.Contains("\"age_band\":\"14+\"") ? "14+" : null;

            if (same && band != null)
            {
                PlayerPrefs.SetString(AgeKey, band);
                PlayerPrefs.SetString(AgeOwnerKey, AccountScope.Who);
            }

            if (same && band != null && text.Contains(wanted))
            {
                PlayerPrefs.SetString(DeviceKey, RequiredVersion);
                PlayerPrefs.SetString(AccountScope.Key(AcceptedKey), RequiredVersion);
                PlayerPrefs.SetString(AccountScope.Key(RecordedKey), RequiredVersion);
                PlayerPrefs.Save();
            }
        }

        Checking = false;

        if (OnAcceptedChanged != null)
            OnAcceptedChanged();
    }

    public void Accept(bool under14)
    {
        PlayerPrefs.SetString(AgeKey, under14 ? "under14" : "14+");
        PlayerPrefs.SetString(AgeOwnerKey, AccountScope.Who);
        PlayerPrefs.SetString(DeviceKey, RequiredVersion);
        PlayerPrefs.SetString(AccountScope.Key(AcceptedKey), RequiredVersion);
        PlayerPrefs.Save();

        if (OnAcceptedChanged != null)
            OnAcceptedChanged();

        StartCoroutine(Record());
    }

    IEnumerator Record()
    {
        SupabaseConfig config = SupabaseConfig.Instance;

        if (config == null || SupabaseAuth.Instance == null ||
            !SupabaseAuth.Instance.IsSignedIn)
        {
            yield break;
        }

        string version = RequiredVersion;
        bool? young = Under14;

        // Sent for this account, so from here on it is this account's answer -
        // including one given on a first run before sign-in had finished.
        if (young.HasValue)
            PlayerPrefs.SetString(AgeOwnerKey, AccountScope.Who);

        string body = "{\"p_version\":\"" + version + "\"" +
                      (young.HasValue ? ",\"p_under14\":" + (young.Value ? "true" : "false") : string.Empty) +
                      "}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/accept_terms"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Not surfaced to the reader. They accepted; the record catching up
                // is the app's problem, and EnsureRecorded tries again on the next
                // session change rather than leaving it unsent for good.
                Debug.LogWarning("[Terms] Could not record acceptance: " + request.error);
                yield break;
            }

            // Only now. Marking it sent before the response comes back would make a
            // failed request indistinguishable from a successful one.
            PlayerPrefs.SetString(AccountScope.Key(RecordedKey), version);
            PlayerPrefs.Save();
        }
    }
}
