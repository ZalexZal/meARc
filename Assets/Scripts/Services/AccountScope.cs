using UnityEngine;

/// <summary>
/// Whose data this is, for anything the phone keeps locally.
///
/// PlayerPrefs is one drawer per phone, and for a long time everything went in
/// it under a bare name: "discovered_&lt;marker&gt;", "marker_progress",
/// "selected_world", "terms_accepted_version". On a phone one person owns that
/// is the same thing as per-person. On a phone a class passes around it is not,
/// and every one of those keys turned out to leak from one walker to the next:
///
///   * the discovery flags handed the next team a finished trail and its route,
///   * the cached answers carried one team's progress into another's sheet,
///   * the chosen trail stayed chosen for somebody who had never joined it,
///   * and the terms flag meant the second person to hold the phone was never
///     shown the terms at all, and no consent was recorded for them - which is
///     a legal record, not a preference.
///
/// Stamping the account onto the key fixes all four in the same way. A guest
/// counts as an account: an anonymous session is a real user with an id of its
/// own, kept across restarts and thrown away on sign-out, so a guest keeps
/// their own walk and takes none of it into the next person's.
///
/// Before a session exists there is no id, and the answer is the "nobody"
/// drawer - empty. That is the safe direction to be wrong in for everything
/// except the terms gate, which must not decide anything until it knows who is
/// asking; <see cref="SupabaseAuth.HasSettled"/> is how it waits.
/// </summary>
public static class AccountScope
{
    /// <summary>The drawer used before anyone is signed in.</summary>
    public const string Nobody = "nobody";

    /// <summary>The current account's id, or <see cref="Nobody"/>.</summary>
    public static string Who
    {
        get
        {
            SupabaseAuth auth = SupabaseAuth.Instance;

            return auth != null && !string.IsNullOrEmpty(auth.UserId)
                ? auth.UserId
                : Nobody;
        }
    }

    /// <summary>The key a given setting is stored under for whoever is signed in.</summary>
    public static string Key(string baseKey)
    {
        return baseKey + "_" + Who;
    }
}
