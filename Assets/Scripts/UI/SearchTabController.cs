using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Search tab.
///
/// The filter pills stay on screen at all times. An earlier version hid them along
/// with the rest of the browse view as soon as a filter was active, which left no
/// way to clear a filter or add a second one.
///
/// Filters combine as OR inside a dimension and AND across them, so "Civic or
/// Religious, from the Socialist or Transition era" is expressible.
///
/// Nothing here needs an AR pose. Distances appear when one is available and are
/// omitted when it isn't, so the tab works indoors and before calibration.
/// </summary>
public class SearchTabController : MonoBehaviour
{
    [Header("Navigation")]
    [Tooltip("Tapping a result has to switch to the Camera tab before the info " +
             "sheet can show, because the sheet lives inside that tab.")]
    public TabNavigationController tabs;
    public CameraTabController cameraTab;

    [Tooltip("Where a result goes. Browse answers what exists; the map is where " +
             "the reader decides what to do about it.")]
    public MapTabController mapTab;

    [Header("Chrome")]
    [Tooltip("Back to the map. Browse has no tab of its own, so without this there " +
             "is no way out of it but the nav bar.")]
    public Button backButton;
    public TextMeshProUGUI backLabel;

    [Header("References")]
    public UserLocationService locationService;
    public TMP_InputField searchField;
    public TextMeshProUGUI searchPlaceholder;

    [Header("Filters - shown only when they have something in them")]
    [Tooltip("The whole section, heading included. A heading with no pills under " +
             "it is a promise the screen does not keep.")]
    public GameObject eraSection;
    public TextMeshProUGUI eraLabel;
    public RectTransform eraPillParent;

    public GameObject typeSection;
    public TextMeshProUGUI typeLabel;
    public RectTransform typePillParent;

    [Header("Browse only")]
    public GameObject closestSection;
    public RectTransform closestParent;
    public TextMeshProUGUI closestLabel;
    public GameObject discoverySection;
    public DiscoveryProgressCard progressCard;

    [Header("Results only")]
    public GameObject resultsSection;
    public RectTransform resultsParent;
    public TextMeshProUGUI emptyText;

    [Header("Prefabs")]
    public BuildingRowView rowPrefab;
    public FilterPillView pillPrefab;

    [Header("Behaviour")]
    [Tooltip("How many buildings the \"Closest to you\" section shows.")]
    public int closestCount = 2;

    /// <summary>Set by the scene so a result can open the info sheet.</summary>
    public Action<BuildingData> onBuildingSelected;

    readonly HashSet<EraDefinition> _activeEras = new HashSet<EraDefinition>();
    readonly HashSet<BuildingCategory> _activeCategories = new HashSet<BuildingCategory>();

    readonly List<FilterPillView> _eraPills = new List<FilterPillView>();
    readonly List<EraDefinition> _eraOrder = new List<EraDefinition>();
    readonly List<FilterPillView> _typePills = new List<FilterPillView>();
    readonly List<BuildingCategory> _typeOrder = new List<BuildingCategory>();

    readonly List<GameObject> _spawned = new List<GameObject>();

    string _query = string.Empty;

    void Awake()
    {
        if (searchField != null)
            searchField.onValueChanged.AddListener(HandleQueryChanged);

        // Wired here rather than by the build tool: Action fields are not a
        // serializable type, so anything assigned at edit time is silently
        // discarded when the scene saves.
        if (onBuildingSelected == null)
            onBuildingSelected = OpenBuilding;

        if (backButton != null)
            backButton.onClick.AddListener(GoBack);
    }

    void GoBack()
    {
        if (tabs != null)
            tabs.ShowMap();
    }

    /// <summary>
    /// Returns to the map with this marker selected and its sheet open.
    ///
    /// The map, not the camera. A result chosen from a list is usually somewhere
    /// the reader has not walked to yet, and dropping them into a camera view of a
    /// street that does not contain it yet is the single most confusing thing this
    /// app could do. The sheet's own Guide me there is the way onward.
    /// </summary>
    void OpenBuilding(BuildingData building)
    {
        if (tabs != null)
            tabs.ShowMap();

        if (mapTab != null)
            mapTab.Focus(building);
    }

    void OnEnable()
    {
        if (locationService == null)
            locationService = UserLocationService.Instance;

        AppStrings.OnLanguageChanged += RebuildAll;
        BuildingDatabase.OnBuildingsChanged += RebuildAll;
        RebuildAll();
    }

    void OnDisable()
    {
        AppStrings.OnLanguageChanged -= RebuildAll;
        BuildingDatabase.OnBuildingsChanged -= RebuildAll;
    }

    void HandleQueryChanged(string value)
    {
        _query = value ?? string.Empty;
        Refresh();
    }

    void RebuildAll()
    {
        ApplyLabels();
        BuildPills();
        Refresh();
    }

    void ApplyLabels()
    {
        if (searchPlaceholder != null)
            searchPlaceholder.text = AppStrings.Get("search.placeholder");

        if (backLabel != null)
            backLabel.text = AppStrings.Get("map.backToMap");

        if (closestLabel != null)
            closestLabel.text = AppStrings.Get("search.closest");

        if (eraLabel != null)
            eraLabel.text = AppStrings.Get("search.byEra");

        if (typeLabel != null)
            typeLabel.text = AppStrings.Get("search.byType");

        if (emptyText != null)
            emptyText.text = AppStrings.Get("search.empty");
    }

    // ------------------------------------------------------------------ pills

    void BuildPills()
    {
        ClearPills(_eraPills);
        _eraOrder.Clear();

        ClearPills(_typePills);
        _typeOrder.Clear();

        foreach (EraDefinition era in BuildingDatabase.AllEras)
        {
            EraDefinition captured = era;
            FilterPillView pill = Instantiate(pillPrefab, eraPillParent);

            pill.Bind(captured.DisplayName, _activeEras.Contains(captured),
                      () => ToggleEra(captured));

            _eraPills.Add(pill);
            _eraOrder.Add(captured);
        }

        foreach (BuildingCategory category in BuildingDatabase.AllCategories)
        {
            BuildingCategory captured = category;
            FilterPillView pill = Instantiate(pillPrefab, typePillParent);

            pill.Bind(captured.DisplayName, _activeCategories.Contains(captured),
                      () => ToggleCategory(captured));

            _typePills.Add(pill);
            _typeOrder.Add(captured);
        }

        // A trail whose stops carry no era and no kind - which is every trail
        // built in the web builder, since neither field is offered there - was
        // showing two headings with nothing underneath them.
        if (eraSection != null)
            eraSection.SetActive(_eraPills.Count > 0);

        if (typeSection != null)
            typeSection.SetActive(_typePills.Count > 0);
    }

    /// <summary>Tapping a pill that is already on turns it off.</summary>
    void ToggleEra(EraDefinition era)
    {
        if (!_activeEras.Remove(era))
            _activeEras.Add(era);

        SyncPillStates();
        Refresh();
    }

    void ToggleCategory(BuildingCategory category)
    {
        if (!_activeCategories.Remove(category))
            _activeCategories.Add(category);

        SyncPillStates();
        Refresh();
    }

    void SyncPillStates()
    {
        for (int i = 0; i < _eraPills.Count && i < _eraOrder.Count; i++)
        {
            if (_eraPills[i] != null)
                _eraPills[i].SetSelected(_activeEras.Contains(_eraOrder[i]));
        }

        for (int i = 0; i < _typePills.Count && i < _typeOrder.Count; i++)
        {
            if (_typePills[i] != null)
                _typePills[i].SetSelected(_activeCategories.Contains(_typeOrder[i]));
        }
    }

    void ClearPills(List<FilterPillView> pills)
    {
        for (int i = 0; i < pills.Count; i++)
        {
            if (pills[i] == null)
                continue;

            // Unparent before destroying: Destroy is deferred to end of frame, and
            // a layout rebuild in between would otherwise measure children that are
            // on their way out.
            pills[i].transform.SetParent(null, false);
            Destroy(pills[i].gameObject);
        }

        pills.Clear();
    }

    // --------------------------------------------------------------- refresh

    public void Refresh()
    {
        bool browsing = string.IsNullOrWhiteSpace(_query) &&
                        _activeEras.Count == 0 &&
                        _activeCategories.Count == 0;

        ClearSpawned();

        // The pill rows are never touched here - they stay visible in both states.
        if (discoverySection != null) discoverySection.SetActive(browsing);
        if (resultsSection != null) resultsSection.SetActive(!browsing);

        if (browsing)
            BuildBrowse();
        else
            BuildResults();
    }

    void BuildBrowse()
    {
        bool hasPose = locationService != null && locationService.HasValidPose;

        // With a pose these are the nearest buildings; without one they are simply
        // suggestions. An earlier version hid the section entirely when there was
        // no pose, which left the browse view looking half-built indoors.
        List<BuildingData> suggestions = hasPose
            ? BuildingDatabase.AllBuildings
                .OrderBy(b => locationService.DistanceMetresTo(b))
                .Take(closestCount)
                .ToList()
            : BuildingDatabase.AllBuildings.Take(closestCount).ToList();

        if (closestSection != null)
            closestSection.SetActive(suggestions.Count > 0);

        if (closestLabel != null)
            closestLabel.text = AppStrings.Get(hasPose ? "search.closest" : "search.suggestions");

        if (closestParent != null)
        {
            foreach (BuildingData building in suggestions)
            {
                double? distance = hasPose
                    ? locationService.DistanceMetresTo(building)
                    : (double?)null;

                SpawnRow(building, closestParent, distance);
            }
        }

        if (progressCard != null)
            progressCard.Refresh();
    }

    void BuildResults()
    {
        if (closestSection != null)
            closestSection.SetActive(false);

        List<BuildingData> results = Filter();

        if (emptyText != null)
        {
            emptyText.gameObject.SetActive(results.Count == 0);

            // Distinguish "nothing matched your filters" from "we never got the
            // building list at all" - they need completely different reactions.
            emptyText.text = BuildingDatabase.LoadState == BuildingDatabase.State.Failed
                ? AppStrings.Get("buildings.offline")
                : AppStrings.Get("search.empty");
        }

        bool hasPose = locationService != null && locationService.HasValidPose;

        foreach (BuildingData building in results)
        {
            double? distance = hasPose ? locationService.DistanceMetresTo(building) : (double?)null;
            SpawnRow(building, resultsParent, distance);
        }
    }

    /// <summary>
    /// OR within each dimension, AND between them: any selected era, and any
    /// selected type, and the text query.
    /// </summary>
    List<BuildingData> Filter()
    {
        string query = _query.Trim();

        return BuildingDatabase.AllBuildings.Where(building =>
        {
            // Matches either language's name, so a Cyrillic query still finds a
            // building whose Bulgarian name hasn't been written yet, and vice versa.
            if (query.Length > 0 && !building.NameMatches(query))
                return false;

            if (_activeEras.Count > 0)
            {
                EraDefinition era = BuildingDatabase.GetEraFor(building);
                if (era == null || !_activeEras.Contains(era))
                    return false;
            }

            if (_activeCategories.Count > 0)
            {
                if (building.category == null || !_activeCategories.Contains(building.category))
                    return false;
            }

            return true;
        }).ToList();
    }

    void SpawnRow(BuildingData building, RectTransform parent, double? distance)
    {
        if (rowPrefab == null || parent == null)
            return;

        BuildingRowView row = Instantiate(rowPrefab, parent);
        BuildingData captured = building;

        row.Bind(captured, distance, () =>
        {
            if (onBuildingSelected != null)
                onBuildingSelected(captured);
        });

        _spawned.Add(row.gameObject);
    }

    void ClearSpawned()
    {
        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] == null)
                continue;

            _spawned[i].transform.SetParent(null, false);
            Destroy(_spawned[i]);
        }

        _spawned.Clear();
    }

    /// <summary>Clears every active filter and the query. Wired to nothing yet.</summary>
    public void ClearAllFilters()
    {
        _activeEras.Clear();
        _activeCategories.Clear();

        if (searchField != null)
            searchField.SetTextWithoutNotify(string.Empty);

        _query = string.Empty;

        SyncPillStates();
        Refresh();
    }
}
