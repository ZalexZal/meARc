using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Markers this reader has chosen not to see.
///
/// Anyone can publish a marker onto a public street, so the reader needs a way to
/// make one go away that does not depend on anybody agreeing with them. It is kept
/// on the device: it works with no account, no connection and no moderator, and it
/// takes effect on the next frame rather than the next review.
///
/// When accounts land this becomes the local half of a server-side block; the
/// stored ids are the same ids, so nothing here has to change.
/// </summary>
public static class HiddenMarkers
{
    const string PrefKey = "hidden_markers";
    const char Separator = '|';

    /// <summary>Raised when the set changes, so the map and the lists can redraw.</summary>
    public static event Action OnChanged;

    static HashSet<string> _hidden;

    public static int Count
    {
        get { Load(); return _hidden.Count; }
    }

    public static bool IsHidden(BuildingData building)
    {
        if (building == null || string.IsNullOrWhiteSpace(building.buildingId))
            return false;

        Load();
        return _hidden.Contains(building.buildingId);
    }

    public static void Hide(BuildingData building)
    {
        if (building == null || string.IsNullOrWhiteSpace(building.buildingId))
            return;

        Load();

        if (!_hidden.Add(building.buildingId))
            return;

        // Guidance to something the reader just chose not to see would leave an
        // arrow pointing at a marker that is no longer on the map.
        if (GuidanceService.Target == building)
            GuidanceService.Stop();

        Save();
    }

    public static void Unhide(BuildingData building)
    {
        if (building == null || string.IsNullOrWhiteSpace(building.buildingId))
            return;

        Load();

        if (_hidden.Remove(building.buildingId))
            Save();
    }

    public static void UnhideAll()
    {
        Load();

        if (_hidden.Count == 0)
            return;

        _hidden.Clear();
        Save();
    }

    static void Load()
    {
        if (_hidden != null)
            return;

        _hidden = new HashSet<string>();

        string stored = PlayerPrefs.GetString(PrefKey, string.Empty);
        if (string.IsNullOrEmpty(stored))
            return;

        string[] parts = stored.Split(Separator);
        for (int i = 0; i < parts.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(parts[i]))
                _hidden.Add(parts[i]);
        }
    }

    static void Save()
    {
        PlayerPrefs.SetString(PrefKey, string.Join(Separator.ToString(), _hidden));
        PlayerPrefs.Save();

        // Re-applies the filter, which raises BuildingDatabase.OnBuildingsChanged
        // and takes the marker off the map, the lists and the AR view at once.
        BuildingDatabase.RebuildVisible();

        if (OnChanged != null)
            OnChanged();
    }
}
