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
/// </summary>
public static class DiscoveryTracker
{
    const string KeyPrefix = "discovered_";

    /// <summary>Raised when a building is discovered for the first time.</summary>
    public static event Action OnDiscoveryChanged;

    public static bool IsDiscovered(BuildingData building)
    {
        if (building == null || string.IsNullOrWhiteSpace(building.buildingId))
            return false;

        return PlayerPrefs.GetInt(KeyPrefix + building.buildingId, 0) == 1;
    }

    public static void MarkDiscovered(BuildingData building)
    {
        if (building == null || string.IsNullOrWhiteSpace(building.buildingId))
            return;

        if (IsDiscovered(building))
            return;

        PlayerPrefs.SetInt(KeyPrefix + building.buildingId, 1);
        PlayerPrefs.Save();

        if (OnDiscoveryChanged != null)
            OnDiscoveryChanged();
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
    /// Settings option.
    /// </summary>
    public static void ResetAllDiscoveries()
    {
        var buildings = BuildingDatabase.AllBuildings;

        for (int i = 0; i < buildings.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(buildings[i].buildingId))
                PlayerPrefs.DeleteKey(KeyPrefix + buildings[i].buildingId);
        }

        PlayerPrefs.Save();

        if (OnDiscoveryChanged != null)
            OnDiscoveryChanged();
    }
}
