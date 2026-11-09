using System;
using System.Collections.Generic;

/// <summary>
/// The numbers on the trail preview, worked out from the stops alone.
///
/// Not the streets: the app does not know them. The route is the straight line
/// from each stop to the next, which undercounts a real walk a little and is
/// honest about it ("~" on the minutes). An ordered trail is measured in its
/// steps; an any-order trail in the order a walker would likely take, always
/// the nearest stop not yet visited, from wherever they start.
/// </summary>
public static class TrailRoute
{
    /// <summary>An unhurried walk with stops, in km/h.</summary>
    public const double WalkingKmPerHour = 4.5;

    public struct Stop
    {
        public double lat;
        public double lng;

        /// <summary>Its place in an ordered trail; 0 or less for "no place".</summary>
        public int step;

        public Stop(double lat, double lng, int step)
        {
            this.lat = lat;
            this.lng = lng;
            this.step = step;
        }
    }

    /// <summary>
    /// Where the trail starts, when the reader's position is unknown: the
    /// lowest step (the first stop when none has one). -1 when there are no stops.
    /// </summary>
    public static int StartIndex(IList<Stop> stops, bool ordered)
    {
        if (stops == null || stops.Count == 0)
            return -1;

        int best = -1;
        for (int i = 0; i < stops.Count; i++)
        {
            if (stops[i].step > 0 && (best < 0 || stops[i].step < stops[best].step))
                best = i;
        }

        return best >= 0 ? best : 0;
    }

    /// <summary>
    /// Where the trail starts for a reader standing at lat/lng: stop one of an
    /// ordered trail wherever they are, and the nearest stop of any other.
    /// </summary>
    public static int StartIndex(IList<Stop> stops, bool ordered, double lat, double lng)
    {
        if (ordered || stops == null || stops.Count == 0)
            return StartIndex(stops, ordered);

        int best = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < stops.Count; i++)
        {
            double d = GeoMath.DistanceMetres(lat, lng, stops[i].lat, stops[i].lng);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// The stops as walked, as indexes into the list. Ordered: by step, stops
    /// with no step last in the order given. Any order: nearest neighbour from
    /// start.
    /// </summary>
    public static List<int> Order(IList<Stop> stops, bool ordered, int start)
    {
        List<int> order = new List<int>();

        if (stops == null || stops.Count == 0)
            return order;

        if (ordered)
        {
            for (int i = 0; i < stops.Count; i++)
                order.Add(i);

            // Stable: List.Sort is not, so the index breaks ties.
            order.Sort((a, b) =>
            {
                int ka = stops[a].step > 0 ? stops[a].step : int.MaxValue;
                int kb = stops[b].step > 0 ? stops[b].step : int.MaxValue;
                return ka != kb ? ka.CompareTo(kb) : a.CompareTo(b);
            });

            return order;
        }

        bool[] used = new bool[stops.Count];
        int at = start >= 0 && start < stops.Count ? start : 0;

        order.Add(at);
        used[at] = true;

        while (order.Count < stops.Count)
        {
            int next = -1;
            double nextDistance = double.MaxValue;

            for (int i = 0; i < stops.Count; i++)
            {
                if (used[i])
                    continue;

                double d = GeoMath.DistanceMetres(stops[at].lat, stops[at].lng,
                                                  stops[i].lat, stops[i].lng);
                if (d < nextDistance)
                {
                    nextDistance = d;
                    next = i;
                }
            }

            order.Add(next);
            used[next] = true;
            at = next;
        }

        return order;
    }

    /// <summary>The sum of the straight legs between the stops, in the order given.</summary>
    public static double LengthMetres(IList<Stop> stops, IList<int> order)
    {
        if (stops == null || order == null)
            return 0d;

        double total = 0d;

        for (int i = 1; i < order.Count; i++)
        {
            Stop a = stops[order[i - 1]];
            Stop b = stops[order[i]];
            total += GeoMath.DistanceMetres(a.lat, a.lng, b.lat, b.lng);
        }

        return total;
    }

    /// <summary>
    /// Minutes on foot at <see cref="WalkingKmPerHour"/>, rounded up to the next
    /// five. Nothing to walk is zero.
    /// </summary>
    public static int WalkingMinutes(double metres)
    {
        if (metres <= 0d)
            return 0;

        double minutes = metres / 1000d / WalkingKmPerHour * 60d;

        // The epsilon keeps an exact 20 from becoming 25 through floating point.
        return (int)Math.Ceiling(minutes / 5d - 1e-9) * 5;
    }
}
