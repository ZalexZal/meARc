using System;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// The sentence the database wrote, where it wrote one.
///
/// PostgREST answers a refusal with a small JSON object carrying a plain
/// message - "a competitive trail needs a real account", "make or join a team
/// first", "no trail with that code". Those are things a walker can act on.
///
/// Three screens had each grown their own copy of this, and the two that had
/// not were the two that mattered on a phone: a hint that would not unlock said
/// "That did not unlock", and a team that could not be made said nothing at
/// all. Both had been told exactly why. One helper, so a fourth screen cannot
/// quietly go without.
/// </summary>
public static class Postgrest
{
    /// <summary>
    /// What the server said, or null if it never answered.
    ///
    /// Null is the only case that really is a connection problem, and it is the
    /// only case a screen should describe as one.
    /// </summary>
    public static string Said(UnityWebRequest request)
    {
        if (request == null || request.responseCode <= 0)
            return null;

        string payload = request.downloadHandler != null
            ? request.downloadHandler.text : null;

        if (string.IsNullOrWhiteSpace(payload))
            return null;

        try
        {
            Body body = JsonUtility.FromJson<Body>(payload);

            if (body != null && !string.IsNullOrWhiteSpace(body.message))
                return body.message;
        }
        catch (Exception)
        {
            // Not a shape we know, which is the same as not having been told.
        }

        return null;
    }

    /// <summary>
    /// Whether this failure is worth one more try in a moment.
    ///
    /// Supabase mints the token on one machine and checks it on another, and
    /// their clocks differ by a fraction of a second - so a token used the
    /// instant it is issued can come back 401 "JWT issued at future". It was
    /// behind the trail list arriving empty, the team card asking somebody to
    /// make a team they already had, and the profile showing nothing: all of
    /// them fired their first request the moment the session appeared. A second
    /// later the same token is fine.
    /// </summary>
    public static bool WorthRetrying(UnityWebRequest request)
    {
        if (request == null || request.responseCode != 401)
            return false;

        string said = Said(request);
        string payload = request.downloadHandler != null ? request.downloadHandler.text : null;

        return (said != null && said.Contains("issued at future"))
            || (payload != null && payload.Contains("issued at future"));
    }

    [Serializable]
    class Body
    {
        public string message;
        public string details;
        public string hint;
        public string code;
    }
}
