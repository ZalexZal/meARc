using System.Collections;
using UnityEngine;

/// <summary>
/// Opens the app on a trail from a link: artrail://join/&lt;trail id&gt;.
///
/// A trail's QR code on the website leads to its card there, and the card's
/// "Open in the app" button sends this. Only public trails can be joined this
/// way - join_listed refuses anything else - so a link is a shortcut to the
/// library, never a way round a join code.
///
/// The link is held until the app is ready to act on it: a session exists and
/// the terms have been accepted. On a first run that means after Welcome and
/// Terms, which a link must not skip.
/// </summary>
public class JoinLinks : MonoBehaviour
{
    const string Prefix = "artrail://join/";

    string _pending;

    void OnEnable()
    {
        Application.deepLinkActivated += Take;
    }

    void OnDisable()
    {
        Application.deepLinkActivated -= Take;
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(Application.absoluteURL))
            Take(Application.absoluteURL);
    }

    void Take(string url)
    {
        if (string.IsNullOrEmpty(url) ||
            !url.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string id = url.Substring(Prefix.Length).Trim('/', ' ');

        // A trail id is a uuid. Anything else is not ours to send to the server.
        if (!System.Guid.TryParse(id, out System.Guid _))
            return;

        bool waiting = _pending != null;
        _pending = id;

        if (!waiting)
            StartCoroutine(JoinWhenReady());
    }

    IEnumerator JoinWhenReady()
    {
        yield return SupabaseAuth.Ready();

        while (TermsService.Instance != null && !TermsService.Instance.Accepted)
            yield return null;

        TrailService trails = TrailService.Instance;
        string id = _pending;
        _pending = null;

        if (trails == null || string.IsNullOrEmpty(id))
            yield break;

        yield return trails.JoinListed(id, ok =>
        {
            if (ok && DoorFlow.Instance != null)
                DoorFlow.Instance.Entered();
            else if (!ok)
                Debug.LogWarning("[Links] Could not join " + id + " from a link.");
        });
    }
}
