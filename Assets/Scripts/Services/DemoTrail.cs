using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The practice trail a new walker is shown instead of a tutorial.
///
/// Three stops made on the phone around wherever it is, every one of them open
/// from where the walker stands: nobody is asked to walk anywhere. The point is
/// to use the real map, the real stop sheet and the real question - once - so
/// the first trail after this is not the first time any of it has been seen.
///
/// Nothing here reaches the server. The stops are not in the database, answers
/// are judged by <see cref="Judge"/>, and nothing is recorded against the
/// walker's account, so a practice answer can never land on a leaderboard.
/// </summary>
public static class DemoTrail
{
    public const string Prefix = "demo_";
    const string SeenKey = "demo_seen";
    const string QuestionId = Prefix + "2";

    /// <summary>The index of the right answer to the one question.</summary>
    const int Correct = 0;

    public static bool Active { get; private set; }

    public static bool HasBeenSeen
    {
        get { return PlayerPrefs.GetInt(SeenKey, 0) == 1; }
    }

    /// <summary>Raised when the demo starts or ends.</summary>
    public static event Action OnChanged;

    public static bool Owns(BuildingData building)
    {
        return building != null && Owns(building.buildingId);
    }

    public static bool Owns(string id)
    {
        return id != null && id.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public static bool IsQuestion(string id)
    {
        return id == QuestionId;
    }

    /// <summary>
    /// Puts the three stops on the map around this point, replacing whatever
    /// trail was drawn there.
    /// </summary>
    public static void Begin(double latitude, double longitude)
    {
        Active = true;

        List<BuildingData> stops = new List<BuildingData>
        {
            Stop(1, latitude, longitude, 34d, 0d),      // north
            Stop(2, latitude, longitude, -12d, 42d),    // east, a little south
            Stop(3, latitude, longitude, -26d, -30d)    // south-west
        };

        stops[1].task = Question();

        BuildingDatabase.Hold(stops);

        // A replay starts from nothing. Only the demo's own stops are on the map
        // at this point, so this clears their flags and nobody else's.
        DiscoveryTracker.ResetAllDiscoveries();

        if (MarkerTaskService.Instance != null)
        {
            for (int i = 0; i < stops.Count; i++)
                MarkerTaskService.Instance.Forget(stops[i].buildingId);
        }

        Raise();
    }

    /// <summary>Takes the stops away and hands the map back to the real trail.</summary>
    public static void End()
    {
        if (!Active)
            return;

        Active = false;
        PlayerPrefs.SetInt(SeenKey, 1);
        PlayerPrefs.Save();

        BuildingDatabase.Release();

        // Practice points are not points. Without this the demo's ten would sit
        // in the total the You tab adds up.
        if (MarkerTaskService.Instance != null)
        {
            for (int step = 1; step <= 3; step++)
                MarkerTaskService.Instance.Forget(Prefix + step);
        }

        RemoteMarkerSource source = UnityEngine.Object.FindAnyObjectByType<RemoteMarkerSource>();
        if (source != null)
            source.Refresh();

        Raise();
    }

    /// <summary>
    /// The verdict check_answer would have sent, in its own shape, so the phone
    /// takes the same path for a practice answer as for a real one.
    /// </summary>
    public static string Judge(string markerId, string answer)
    {
        bool right = IsQuestion(markerId) && answer == Correct.ToString();

        return right
            ? "{\"verdict\":\"correct\",\"points\":10}"
            : "{\"verdict\":\"incorrect\",\"deducted\":0,\"cooldown_seconds\":0}";
    }

    static BuildingData Stop(int step, double latitude, double longitude,
                             double north, double east)
    {
        BuildingData stop = ScriptableObject.CreateInstance<BuildingData>();
        string key = "demo." + step;

        stop.buildingId = Prefix + step;
        stop.step = step;
        stop.isPublished = true;
        stop.buildingName = AppStrings.GetIn(AppLanguage.English, key + ".title");
        stop.buildingNameBg = AppStrings.GetIn(AppLanguage.Bulgarian, key + ".title");
        stop.description = AppStrings.GetIn(AppLanguage.English, key + ".body");
        stop.descriptionBg = AppStrings.GetIn(AppLanguage.Bulgarian, key + ".body");

        // Metres to degrees. Good to well under a metre at these distances.
        stop.latitude = latitude + north / 111320d;
        stop.longitude = longitude + east / (111320d * Math.Cos(latitude * Math.PI / 180d));

        stop.radiusMetres = 25;
        stop.markerColourHex = "#0066CC";
        return stop;
    }

    static MarkerTask Question()
    {
        return new MarkerTask
        {
            kind = "choice",
            prompt = Both("demo.2.q"),
            options = new[] { Both("demo.2.a1"), Both("demo.2.a2"), Both("demo.2.a3") },
            points = 10,
            on_wrong = "retry"
        };
    }

    static LocalisedText Both(string key)
    {
        return new LocalisedText
        {
            en = AppStrings.GetIn(AppLanguage.English, key),
            bg = AppStrings.GetIn(AppLanguage.Bulgarian, key)
        };
    }

    static void Raise()
    {
        if (OnChanged != null)
            OnChanged();
    }
}
