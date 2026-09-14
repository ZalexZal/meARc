using System;
using UnityEngine;

/// <summary>
/// Counts how many distinct buildings the reader has ever opened. A building counts
/// the first time its info panel opens, whether that came from an AR marker, a
/// Nearby row or a search result.
///
/// It stores one flag per building and nothing else - no timestamps, no location
/// history, no analytics. It is a quiet sense of progress, not a game: there are no
/// rewards, levels, streaks or scores, and it never prompts.
///
/// The flags belong to an account, not to the phone. See <see cref="Key"/> for why
/// that distinction turned out to matter.
/// </summary>
public static class DiscoveryTracker
{
    const string KeyPrefix = "discovered_";

    /// <summary>Raised when a building is discovered for the first time.</summary>
    public static event Action OnDiscoveryChanged;

    /// <summary>
    /// The key for one building, stamped with whose walk it is.
    ///
    /// These flags used to be stored per phone rather than per walker, and on a
    /// phone only one person ever uses that is the same thing. On a shared one it
    /// is not: signing out left every flag behind, so the next person opened the
    /// map on "All 1 found - Trail complete" for a trail they had never walked.
    /// On a competitive trail that is the route handed to the next team, and it
    /// sat directly under a screen promising that "signing in to an account you
    /// already have does not carry over what you walked on this phone".
    ///
    /// A guest counts as a walker here. An anonymous session is a real account
    /// with an id of its own, kept across restarts and thrown away on sign-out,
    /// so a guest keeps their walk while they are that guest and takes nothing
    /// into the next person's.
    ///
    /// Before the session lands there is no id, so the answer is an empty bucket -
    /// nothing found. That is the safe direction to be wrong in: it under-reports
    /// for a moment rather than showing one walker another's progress, and the
    /// screens redraw as soon as the session arrives.
    /// </summary>
    static string Key(string buildingId)
    {
        SupabaseAuth auth = SupabaseAuth.Instance;

        string who = auth != null && !string.IsNullOrEmpty(auth.UserId)
            ? auth.UserId
            : "nobody";

        return KeyPrefix + who + "_" + buildingId;
    }

    /// <summary>
    /// Redraws the screens that read these flags when the walker changes.
    ///
    /// Signing in or out changes every answer this class gives, and the map's
    /// strip and progress card have no other reason to look again. Subscribed
    /// once for the life of the process, because a static class has no OnDisable
    /// to unsubscribe in.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void FollowTheSession()
    {
        SupabaseAuth.OnSessionChanged += Raise;
    }

    static void Raise()
    {
        if (OnDiscoveryChanged != null)
            OnDiscoveryChanged();
    }

    public static bool IsDiscovered(BuildingData building)
    {
        if (building == null || string.IsNullOrWhiteSpace(building.buildingId))
            return false;

        return PlayerPrefs.GetInt(Key(building.buildingId), 0) == 1;
    }

    public static void MarkDiscovered(BuildingData building)
    {
        if (building == null || string.IsNullOrWhiteSpace(building.buildingId))
            return;

        if (IsDiscovered(building))
            return;

        PlayerPrefs.SetInt(Key(building.buildingId), 1);
        PlayerPrefs.Save();

        Raise();
    }

    public static int DiscoveredCount
    {
        get
        {
            int count = 0;
            var buildings = BuildingDatabase.AllBuildings;

            for (int i = 0; i < buildings.Count; i++)
            {
                if (IsDiscovered(buildings[i]))
                    count++;
            }

            return count;
        }
    }

    public static int TotalCount
    {
        get { return BuildingDatabase.AllBuildings.Count; }
    }

    /// <summary>
    /// Not surfaced in the UI yet - kept for a debug button and a possible future
    /// Settings option. Clears the signed-in walker's flags, not the phone's.
    /// </summary>
    public static void ResetAllDiscoveries()
    {
        var buildings = BuildingDatabase.AllBuildings;

        for (int i = 0; i < buildings.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(buildings[i].buildingId))
                PlayerPrefs.DeleteKey(Key(buildings[i].buildingId));
        }

        PlayerPrefs.Save();

        Raise();
    }
}
