using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Whether this reader has agreed to the current terms.
///
/// Recorded in two places on purpose. The device remembers so the screen does not
/// reappear on every launch with no signal; the database remembers so there is an
/// actual record of what was agreed and when. The device copy is a convenience
/// and the server copy is the truth - if they disagree, the screen shows again.
///
/// Anonymous readers accept too. Somebody who looked at a trail without making an
/// account still agreed to something, and Phase 1 is where that stops being
/// hand-waved.
/// </summary>
public class TermsService : MonoBehaviour
{
    const string AcceptedKey = "terms_accepted_version";

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
        get { return PlayerPrefs.GetString(AcceptedKey, string.Empty) == RequiredVersion; }
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

    /// <summary>
    /// Records the acceptance locally at once and on the server when it can.
    ///
    /// The local write does not wait for the network. Someone who has read the
    /// terms and pressed the button has accepted them; making that depend on a
    /// signal would mean showing the same screen again in a tunnel.
    /// </summary>
    public void Accept()
    {
        PlayerPrefs.SetString(AcceptedKey, RequiredVersion);
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

        string body = "{\"p_version\":\"" + RequiredVersion + "\"}";

        using (UnityWebRequest request =
               SupabaseAuth.Post(config.Endpoint("/rest/v1/rpc/accept_terms"), body, null))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Not surfaced to the reader. They accepted; the record catching up
                // is the app's problem, and it is retried on the next acceptance.
                Debug.LogWarning("[Terms] Could not record acceptance: " + request.error);
            }
        }
    }
}
