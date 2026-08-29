using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// The single place that answers "what buildings exist".
///
/// Buildings now come from the published spreadsheet at runtime, so this holds
/// whatever the source last handed it. Categories and eras still ship inside the
/// build - there are a dozen of them, they carry marker prefabs, and they change
/// far less often than the buildings do.
/// </summary>
public static class BuildingDatabase
{
    public enum State
    {
        NotLoaded,
        Loading,
        Ready,
        Failed
    }

    public const string CategoriesPath = "Categories";
    public const string ErasPath = "Eras";

    /// <summary>Raised when the building list is replaced, so views can rebuild.</summary>
    public static event Action OnBuildingsChanged;

    public static State LoadState { get; private set; } = State.NotLoaded;

    static List<BuildingData> _all = new List<BuildingData>();
    static List<BuildingData> _visible = new List<BuildingData>();
    static Dictionary<string, BuildingData> _byId = new Dictionary<string, BuildingData>();

    static List<BuildingCategory> _categories;
    static List<EraDefinition> _eras;

    /// <summary>Published buildings, plus drafts when developer mode is on.</summary>
    public static IReadOnlyList<BuildingData> AllBuildings
    {
        get { return _visible; }
    }

    /// <summary>Everything loaded, drafts included. For tooling, not for the UI.</summary>
    public static IReadOnlyList<BuildingData> AllBuildingsIncludingDrafts
    {
        get { return _all; }
    }

    public static IReadOnlyList<BuildingCategory> AllCategories
    {
        get { EnsureAssetsLoaded(); return _categories; }
    }

    public static IReadOnlyList<EraDefinition> AllEras
    {
        get { EnsureAssetsLoaded(); return _eras; }
    }

    // ------------------------------------------------------------------ loading

    public static void SetLoading()
    {
        LoadState = State.Loading;
    }

    public static void SetFailed()
    {
        // Only a failure if there is nothing to show. A refresh that fails while a
        // cached list is already loaded is not worth telling the reader about.
        if (_all.Count == 0)
            LoadState = State.Failed;

        Raise();
    }

    public static void SetBuildings(IEnumerable<BuildingData> buildings)
    {
        _all = buildings != null ? buildings.ToList() : new List<BuildingData>();
        LoadState = State.Ready;

        RebuildVisible();
    }

    /// <summary>
    /// Re-applies the filters and tells everything listening. Called after the list
    /// is replaced, after developer mode is toggled, and after the reader hides or
    /// unhides a marker.
    /// </summary>
    public static void RebuildVisible()
    {
        bool showDrafts = DeveloperMode.ShowDrafts;

        _visible = _all
            .Where(b => b != null && (b.isPublished || showDrafts))
            .Where(b => !HiddenMarkers.IsHidden(b))
            .ToList();

        _byId = new Dictionary<string, BuildingData>();
        foreach (BuildingData building in _visible)
        {
            if (string.IsNullOrWhiteSpace(building.buildingId))
            {
                Debug.LogWarning($"BuildingDatabase: \"{building.name}\" has no id, so it " +
                                 "can't be tracked. Check the sheet's id column.", building);
                continue;
            }

            if (_byId.ContainsKey(building.buildingId))
            {
                Debug.LogWarning($"BuildingDatabase: duplicate id \"{building.buildingId}\" - " +
                                 "only the first row with it is reachable.", building);
                continue;
            }

            _byId.Add(building.buildingId, building);
        }

        Raise();
    }

    static void Raise()
    {
        if (OnBuildingsChanged != null)
            OnBuildingsChanged();
    }

    // ------------------------------------------------------------------ lookups

    public static BuildingData GetById(string buildingId)
    {
        if (string.IsNullOrWhiteSpace(buildingId))
            return null;

        _byId.TryGetValue(buildingId, out BuildingData building);
        return building;
    }

    /// <summary>Matches a sheet's category cell to a Category asset, case-insensitively.</summary>
    public static BuildingCategory FindCategory(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
            return null;

        EnsureAssetsLoaded();

        string wanted = categoryName.Trim();

        return _categories.FirstOrDefault(c =>
            string.Equals(c.categoryName, wanted, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The era whose span contains this building's year, or null when the year is
    /// unknown - which keeps undated buildings out of era filters rather than
    /// silently bucketing them into the earliest era.
    /// </summary>
    public static EraDefinition GetEraFor(BuildingData building)
    {
        EnsureAssetsLoaded();

        if (building == null || building.yearBuiltNumeric == 0)
            return null;

        for (int i = 0; i < _eras.Count; i++)
        {
            if (_eras[i].Contains(building.yearBuiltNumeric))
                return _eras[i];
        }

        return null;
    }

    static void EnsureAssetsLoaded()
    {
        if (_categories != null)
            return;

        _categories = Resources.LoadAll<BuildingCategory>(CategoriesPath).ToList();

        _eras = Resources.LoadAll<EraDefinition>(ErasPath)
            .OrderBy(era => era.sortOrder)
            .ToList();
    }
}
