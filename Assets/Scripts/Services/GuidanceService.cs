using System;
using UnityEngine;

/// <summary>
/// Holds the one thing the reader asked to be guided to.
///
/// Static on purpose. "Guide me there" has to survive the screen it was started
/// from - that is the whole promise of the feature - and the map and the camera
/// live on two different panels that are enabled and disabled as tabs change. A
/// component on either of them would lose the target the moment the reader
/// switched, which is exactly the bug this replaces.
/// </summary>
public static class GuidanceService
{
    /// <summary>Raised when guidance starts, stops, or changes target.</summary>
    public static event Action OnGuidanceChanged;

    public static BuildingData Target { get; private set; }

    public static bool IsGuiding
    {
        get { return Target != null; }
    }

    public static void Guide(BuildingData building)
    {
        if (building == null)
        {
            Stop();
            return;
        }

        if (Target == building)
            return;

        Target = building;
        Raise();
    }

    public static void Stop()
    {
        if (Target == null)
            return;

        Target = null;
        Raise();
    }

    /// <summary>
    /// Drops the target if it is no longer in the database - the trail was left,
    /// or the marker was unpublished while the reader was walking to it. Without
    /// this the banner would point at a building that no longer exists.
    /// </summary>
    public static void Validate()
    {
        if (Target == null)
            return;

        var all = BuildingDatabase.AllBuildings;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] == Target)
                return;
        }

        Stop();
    }

    static void Raise()
    {
        if (OnGuidanceChanged != null)
            OnGuidanceChanged();
    }
}
