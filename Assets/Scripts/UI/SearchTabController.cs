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

    [Header("Browse only")]
    public GameObject closestSection;
    public RectTransform closestParent;
    public TextMeshProUGUI closestLabel;
    public GameObject discoverySection;
    public DiscoveryProgressCard progressCard;

    [Tooltip("The whole trail as a picture. Shown alongside the summary and put " +
             "away while somebody is searching, because a search is a question " +
             "about one marker and the graph is the answer about all of them.")]
    public GameObject graphSection;
    public TrailGraphView graph;

    [Header("Results only")]
    public GameObject resultsSection;
    public RectTransform resultsParent;
    public TextMeshProUGUI emptyText;

    [Header("Prefabs")]
    public BuildingRowView rowPrefab;

    [Header("Behaviour")]
    [Tooltip("How many buildings the \"Closest to you\" section shows.")]
    public int closestCount = 2;

    /// <summary>Set by the scene so a result can open the info sheet.</summary>
    public Action<BuildingData> onBuildingSelected;


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
        // Found if it was never wired. This is the only way off this screen
        // besides the nav bar, and a null reference here is a reader stuck on a
        // list with no exit - which is worth one lookup to make impossible.
        if (tabs == null)
            tabs = FindAnyObjectByType<TabNavigationController>();

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

        // Tapping a node does what tapping a result does: back to the map, with
        // that marker selected and its sheet open.
        if (graph != null)
            graph.onOpen = OpenBuilding;

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


        if (emptyText != null)
            emptyText.text = AppStrings.Get("search.empty");
    }

    // ------------------------------------------------------------------ pills

    // --------------------------------------------------------------- refresh

    public void Refresh()
    {
        bool browsing = string.IsNullOrWhiteSpace(_query);

        ClearSpawned();

        if (discoverySection != null) discoverySection.SetActive(browsing);
        if (graphSection != null) graphSection.SetActive(browsing);
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

    /// <summary>Clears the query. Wired to nothing yet.</summary>
    public void ClearAllFilters()
    {
        if (searchField != null)
            searchField.SetTextWithoutNotify(string.Empty);

        _query = string.Empty;

        Refresh();
    }
}
