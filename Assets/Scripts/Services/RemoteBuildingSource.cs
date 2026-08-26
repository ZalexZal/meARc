using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Loads buildings from the published Google Sheet.
///
/// Order of operations on launch: show the cached copy immediately so the app is
/// usable in the first second, then fetch in the background and replace it. A
/// refresh that fails is silent - the reader keeps whatever was already there.
/// Only a first run with no cache and no network surfaces an error.
/// </summary>
public class RemoteBuildingSource : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("The sheet's Publish-to-web CSV link. File > Share > Publish to web, " +
             "pick the tab, choose Comma-separated values.")]
    [TextArea(2, 4)]
    public string sheetUrl =
        "https://docs.google.com/spreadsheets/d/e/2PACX-1vS9SjrzUVx7LWubKqj58JLw_jpgbutPT_EO-UnOZWRryYBFPs8x77Pbxn8NftGWg49dQaF_Qnw9ZLcY/pub?gid=0&single=true&output=csv";

    [Header("Behaviour")]
    [Tooltip("Seconds before a fetch is abandoned and the cache is kept.")]
    public int timeoutSeconds = 20;

    [Tooltip("Refetch when the app returns from the background, so a building " +
             "published while the phone was pocketed appears without a restart.")]
    public bool refreshOnResume = true;

    [Tooltip("Ignore a resume refresh if the last one was this recent.")]
    public float minSecondsBetweenFetches = 30f;

    const string CacheFileName = "buildings.csv";

    float _lastFetchTime = -999f;

    string CachePath
    {
        get { return Path.Combine(Application.persistentDataPath, CacheFileName); }
    }

    void Start()
    {
        BuildingDatabase.SetLoading();
        LoadFromCache();
        StartCoroutine(Fetch());
    }

    void OnEnable()
    {
        AppLifecycleWatcher.OnResumed += HandleResumed;
    }

    void OnDisable()
    {
        AppLifecycleWatcher.OnResumed -= HandleResumed;
    }

    void HandleResumed(float secondsBackgrounded)
    {
        if (!refreshOnResume)
            return;

        if (Time.unscaledTime - _lastFetchTime < minSecondsBetweenFetches)
            return;

        StartCoroutine(Fetch());
    }

    // -------------------------------------------------------------------- load

    void LoadFromCache()
    {
        if (!File.Exists(CachePath))
            return;

        try
        {
            Publish(File.ReadAllText(CachePath), "cache");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("Couldn't read the cached building list: " + exception.Message);
        }
    }

    IEnumerator Fetch()
    {
        if (string.IsNullOrWhiteSpace(sheetUrl))
        {
            Debug.LogError("RemoteBuildingSource: no sheet URL set.");
            BuildingDatabase.SetFailed();
            yield break;
        }

        _lastFetchTime = Time.unscaledTime;

        using (UnityWebRequest request = UnityWebRequest.Get(sheetUrl))
        {
            request.timeout = timeoutSeconds;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                // Not an error the reader needs to see if they already have data.
                Debug.LogWarning("Building list fetch failed: " + request.error);
                BuildingDatabase.SetFailed();
                yield break;
            }

            string csv = request.downloadHandler.text;

            if (!Publish(csv, "network"))
                yield break;

            try
            {
                File.WriteAllText(CachePath, csv);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("Couldn't cache the building list: " + exception.Message);
            }
        }
    }

    /// <summary>Parses the CSV and hands the result to the database.</summary>
    bool Publish(string csv, string origin)
    {
        // A published sheet that has been unshared returns an HTML error page with
        // a 200, so check that this actually looks like our data.
        if (string.IsNullOrWhiteSpace(csv) || csv.TrimStart().StartsWith("<"))
        {
            Debug.LogWarning($"Building list from {origin} wasn't CSV. Is the sheet " +
                             "still published to the web?");
            return false;
        }

        List<Dictionary<string, string>> rows = CsvParser.Parse(csv);
        List<BuildingData> buildings = new List<BuildingData>();

        foreach (var row in rows)
        {
            BuildingData building = BuildRow(row);
            if (building != null)
                buildings.Add(building);
        }

        Debug.Log($"[Buildings] Loaded {buildings.Count} from {origin}.");
        BuildingDatabase.SetBuildings(buildings);
        return true;
    }

    /// <summary>
    /// Turns one sheet row into a BuildingData. These are created at runtime rather
    /// than loaded as assets, which is what lets every existing screen keep taking
    /// a BuildingData without a single change.
    /// </summary>
    static BuildingData BuildRow(Dictionary<string, string> row)
    {
        string id = Get(row, "id");

        // A row with no id or no position can't be tracked or placed, so it is
        // skipped rather than half-shown.
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (!TryGetDouble(row, "lat", out double latitude) ||
            !TryGetDouble(row, "lng", out double longitude))
        {
            Debug.LogWarning($"[Buildings] \"{id}\" has no usable coordinates - skipped.");
            return null;
        }

        BuildingData building = ScriptableObject.CreateInstance<BuildingData>();
        building.name = id;

        building.buildingId = id;
        building.isPublished = string.Equals(Get(row, "status"), "published",
                                             System.StringComparison.OrdinalIgnoreCase);

        building.buildingName = Get(row, "name_en");
        building.buildingNameBg = Get(row, "name_bg");
        building.description = Get(row, "desc_en");
        building.descriptionBg = Get(row, "desc_bg");
        building.yearBuilt = Get(row, "year");
        building.yearBuiltBg = Get(row, "year_bg");
        building.architect = Get(row, "architect_en");
        building.architectBg = Get(row, "architect_bg");
        building.externalLinkURL = Get(row, "link_en");
        building.externalLinkURLBg = Get(row, "link_bg");

        building.markerStyle = Get(row, "marker");
        building.markerColourHex = Get(row, "marker_colour");

        building.latitude = latitude;
        building.longitude = longitude;

        if (int.TryParse(Get(row, "year_numeric"), NumberStyles.Integer,
                         CultureInfo.InvariantCulture, out int year))
        {
            building.yearBuiltNumeric = year;
        }

        building.category = BuildingDatabase.FindCategory(Get(row, "category"));

        return building;
    }

    static string Get(Dictionary<string, string> row, string key)
    {
        return row.TryGetValue(key, out string value) ? value : string.Empty;
    }

    /// <summary>
    /// Always parsed as invariant. On a phone set to Bulgarian the current culture
    /// reads "." as a thousands separator, which turns 42.48 into 4248.
    /// </summary>
    static bool TryGetDouble(Dictionary<string, string> row, string key, out double value)
    {
        return double.TryParse(Get(row, key), NumberStyles.Float,
                               CultureInfo.InvariantCulture, out value);
    }
}
