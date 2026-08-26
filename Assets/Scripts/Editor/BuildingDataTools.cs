using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Google.XR.ARCoreExtensions.GeospatialCreator;

/// <summary>
/// Editor-only helpers that keep BuildingData assets in agreement with the
/// Geospatial Creator anchors placed in the scene. Nothing here ships in a build.
/// </summary>
public static class BuildingDataTools
{
    /// <summary>
    /// Copies Latitude/Longitude from every anchor in the open scene into the
    /// BuildingData asset that its TappableObject points at, so the coordinates
    /// never have to be transcribed by hand.
    /// </summary>
    [MenuItem("Tools/Building Data/Sync Coordinates From Anchors")]
    public static void SyncCoordinatesFromAnchors()
    {
        TappableObject[] tappables =
            Object.FindObjectsByType<TappableObject>(FindObjectsInactive.Include);

        if (tappables.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Sync Coordinates",
                "No TappableObject components were found in the open scene.",
                "OK");
            return;
        }

        int updated = 0;
        int alreadyCorrect = 0;
        List<string> skipped = new List<string>();

        foreach (TappableObject tappable in tappables)
        {
            if (tappable.buildingData == null)
            {
                skipped.Add($"{tappable.name}: no Building Data assigned");
                continue;
            }

            ARGeospatialCreatorAnchor anchor = tappable.GetComponent<ARGeospatialCreatorAnchor>();
            if (anchor == null)
            {
                skipped.Add($"{tappable.name}: no AR Geospatial Creator Anchor on the same object");
                continue;
            }

            if (tappable.buildingData.latitude == anchor.Latitude &&
                tappable.buildingData.longitude == anchor.Longitude)
            {
                alreadyCorrect++;
                continue;
            }

            // A Geospatial Creator anchor can silently reset itself to Google's
            // default sample coordinates. Copying that over good data would destroy
            // it, so refuse any jump large enough to be a different city.
            if (tappable.buildingData.latitude != 0d || tappable.buildingData.longitude != 0d)
            {
                double jump = GeoMath.DistanceMetres(
                    tappable.buildingData.latitude, tappable.buildingData.longitude,
                    anchor.Latitude, anchor.Longitude);

                if (jump > 1000d)
                {
                    skipped.Add(
                        $"{tappable.name}: anchor is {jump / 1000d:F0} km from the saved " +
                        $"coordinates ({anchor.Latitude:F5}, {anchor.Longitude:F5} vs " +
                        $"{tappable.buildingData.latitude:F5}, {tappable.buildingData.longitude:F5}). " +
                        "Refused - fix the anchor, don't overwrite the data.");
                    continue;
                }
            }

            Undo.RecordObject(tappable.buildingData, "Sync Building Coordinates");
            tappable.buildingData.latitude = anchor.Latitude;
            tappable.buildingData.longitude = anchor.Longitude;
            EditorUtility.SetDirty(tappable.buildingData);
            updated++;
        }

        AssetDatabase.SaveAssets();

        // "Updated 0" on its own reads like a failure, so spell out that nothing
        // needing a change is the good outcome.
        string report;
        if (updated == 0 && skipped.Count == 0)
            report = $"All {alreadyCorrect} building(s) already match their anchors. " +
                     "Nothing needed changing.";
        else
            report = $"Updated {updated} building(s). " +
                     $"{alreadyCorrect} already matched.";

        if (skipped.Count > 0)
            report += "\n\nSkipped:\n" + string.Join("\n", skipped);

        Debug.Log("[Building Data] " + report);
        EditorUtility.DisplayDialog("Sync Coordinates", report, "OK");
    }

    /// <summary>
    /// Reports every BuildingData asset with data that would break the Nearby list,
    /// the Search list, or the Learn More button once the app ships.
    /// </summary>
    [MenuItem("Tools/Building Data/Validate All Buildings")]
    public static void ValidateAllBuildings()
    {
        BuildingData[] all = Resources.LoadAll<BuildingData>("BuildingData");
        List<string> problems = new List<string>();
        List<string> untranslated = new List<string>();

        foreach (BuildingData building in all)
        {
            string label = building.name;

            if (string.IsNullOrWhiteSpace(building.buildingId))
                problems.Add($"{label}: Building Id is empty — Localization has no key to look up");
            else if (building.buildingId.Trim() != building.buildingId ||
                     building.buildingId.Any(char.IsWhiteSpace) ||
                     building.buildingId.Any(char.IsUpper))
                problems.Add($"{label}: Building Id \"{building.buildingId}\" should be lowercase " +
                             "with no spaces");

            if (string.IsNullOrWhiteSpace(building.buildingName))
                problems.Add($"{label}: Building Name is empty");

            if (building.latitude == 0d && building.longitude == 0d)
                problems.Add($"{label}: coordinates are 0,0 — it will never appear in Nearby");

            if (string.IsNullOrWhiteSpace(building.description))
                problems.Add($"{label}: Description is empty");

            if (string.IsNullOrWhiteSpace(building.buildingNameBg))
                untranslated.Add($"{label}: no Bulgarian name");

            if (string.IsNullOrWhiteSpace(building.descriptionBg))
                untranslated.Add($"{label}: no Bulgarian description");
        }

        // Two buildings sharing an id would collide in saved discovery progress,
        // so this matters more than a duplicated display name.
        foreach (var duplicate in all
                     .Where(b => !string.IsNullOrWhiteSpace(b.buildingId))
                     .GroupBy(b => b.buildingId.Trim().ToLowerInvariant())
                     .Where(g => g.Count() > 1))
        {
            problems.Add($"Duplicate Building Id \"{duplicate.First().buildingId}\" " +
                         $"on {duplicate.Count()} assets");
        }

        foreach (var duplicate in all
                     .Where(b => !string.IsNullOrWhiteSpace(b.buildingName))
                     .GroupBy(b => b.buildingName.Trim().ToLowerInvariant())
                     .Where(g => g.Count() > 1))
        {
            problems.Add($"Duplicate Building Name \"{duplicate.First().buildingName}\" " +
                         $"on {duplicate.Count()} assets");
        }

        string report = problems.Count == 0
            ? $"All {all.Length} building(s) look good."
            : $"Checked {all.Length} building(s), found {problems.Count} problem(s):\n\n" +
              string.Join("\n", problems);

        // Listed separately from problems: a building reads perfectly well in
        // English while its Bulgarian is still being written, so this is a
        // to-do list rather than a fault.
        if (untranslated.Count > 0)
        {
            report += $"\n\nBulgarian still to write ({untranslated.Count}):\n" +
                      string.Join("\n", untranslated.Take(20));

            if (untranslated.Count > 20)
                report += $"\n...and {untranslated.Count - 20} more";
        }

        Debug.Log("[Building Data] " + report);
        EditorUtility.DisplayDialog("Validate Buildings", report, "OK");
    }
}
