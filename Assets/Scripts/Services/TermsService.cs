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
    void EnsureRecorded()
    {
        if (!Accepted)
            return;

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
    public void Accept()
    {
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
        string body = "{\"p_version\":\"" + version + "\"}";

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
