using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// The trail preview's numbers: how long the route is, how long it takes on
/// foot, and which order an any-order trail is measured in.
/// </summary>
public class TrailRouteTests
{
    static TrailRoute.Stop S(double lat, double lng, int step)
    {
        return new TrailRoute.Stop(lat, lng, step);
    }

    // ------------------------------------------------------------ walking time

    [Test]
    public void NoRouteIsNoMinutes()
    {
        Assert.AreEqual(0, TrailRoute.WalkingMinutes(0d));
        Assert.AreEqual(0, TrailRoute.WalkingMinutes(-5d));
    }

    [Test]
    public void MinutesRoundUpToFive()
    {
        // 1.4 km at 4.5 km/h is 18.7 min.
        Assert.AreEqual(20, TrailRoute.WalkingMinutes(1400d));
        // 100 m is 1.3 min: still five.
        Assert.AreEqual(5, TrailRoute.WalkingMinutes(100d));
        // 3 km is exactly 40 min, and stays 40.
        Assert.AreEqual(40, TrailRoute.WalkingMinutes(3000d));
        // 1.5 km is exactly 20 min, despite floating point.
        Assert.AreEqual(20, TrailRoute.WalkingMinutes(1500d));
        // Ten metres past it is the next five.
        Assert.AreEqual(25, TrailRoute.WalkingMinutes(1510d));
        // 375 m is exactly 5 min, and stays 5.
        Assert.AreEqual(5, TrailRoute.WalkingMinutes(375d));
    }

    // ------------------------------------------------------------------ length

    [Test]
    public void LengthIsTheSumOfStraightLegs()
    {
        var stops = new List<TrailRoute.Stop>
        {
            S(42.00, 26.0, 1), S(42.01, 26.0, 2), S(42.02, 26.0, 3)
        };

        double length = TrailRoute.LengthMetres(stops, new List<int> { 0, 1, 2 });

        // 0.01 degree of latitude is about 1112 m; two of them.
        Assert.AreEqual(2224d, length, 3d);
        Assert.AreEqual(GeoMath.DistanceMetres(42.00, 26.0, 42.01, 26.0) +
                        GeoMath.DistanceMetres(42.01, 26.0, 42.02, 26.0), length, 1e-6);
    }

    [Test]
    public void LengthFollowsTheOrderGiven()
    {
        var stops = new List<TrailRoute.Stop>
        {
            S(42.00, 26.0, 1), S(42.02, 26.0, 2), S(42.01, 26.0, 3)
        };

        // There and back again is longer than the straight walk.
        Assert.Greater(TrailRoute.LengthMetres(stops, new List<int> { 0, 1, 2 }),
                       TrailRoute.LengthMetres(stops, new List<int> { 0, 2, 1 }) + 1000d);
    }

    [Test]
    public void OneStopOrNoneHasNoLength()
    {
        var one = new List<TrailRoute.Stop> { S(42.0, 26.0, 1) };
        Assert.AreEqual(0d, TrailRoute.LengthMetres(one, new List<int> { 0 }));
        Assert.AreEqual(0d, TrailRoute.LengthMetres(new List<TrailRoute.Stop>(), new List<int>()));
    }

    // ------------------------------------------------------------------- order

    [Test]
    public void InOrderFollowsTheSteps()
    {
        var stops = new List<TrailRoute.Stop>
        {
            S(42.0, 26.0, 3), S(42.0, 26.1, 1), S(42.0, 26.2, 2), S(42.0, 26.3, 0)
        };

        // Step 0 (no place in the order) goes last.
        CollectionAssert.AreEqual(new[] { 1, 2, 0, 3 },
                                  TrailRoute.Order(stops, true, TrailRoute.StartIndex(stops, true)));
    }

    [Test]
    public void AnyOrderIsNearestNeighbourFromTheStart()
    {
        // On one line of latitude, at longitudes 0, .03, .01, .02 past 26.
        var stops = new List<TrailRoute.Stop>
        {
            S(42.0, 26.00, 1), S(42.0, 26.03, 2), S(42.0, 26.01, 3), S(42.0, 26.02, 4)
        };

        CollectionAssert.AreEqual(new[] { 0, 2, 3, 1 }, TrailRoute.Order(stops, false, 0));

        // From the far end it walks back the other way.
        CollectionAssert.AreEqual(new[] { 1, 3, 2, 0 }, TrailRoute.Order(stops, false, 1));
    }

    [Test]
    public void StartIsStepOneInOrder()
    {
        var stops = new List<TrailRoute.Stop>
        {
            S(42.0, 26.0, 2), S(42.0, 26.1, 1)
        };

        Assert.AreEqual(1, TrailRoute.StartIndex(stops, true));
        // Where the reader stands does not move the start of an ordered trail.
        Assert.AreEqual(1, TrailRoute.StartIndex(stops, true, 42.0, 26.0));
    }

    [Test]
    public void StartIsTheNearestStopInAnyOrder()
    {
        var stops = new List<TrailRoute.Stop>
        {
            S(42.0, 26.0, 1), S(42.0, 26.1, 2), S(42.0, 26.2, 3)
        };

        Assert.AreEqual(2, TrailRoute.StartIndex(stops, false, 42.0, 26.19));
        // Not located: the lowest step.
        Assert.AreEqual(0, TrailRoute.StartIndex(stops, false));
    }

    [Test]
    public void OneStopIsItsOwnOrder()
    {
        var one = new List<TrailRoute.Stop> { S(42.0, 26.0, 1) };

        CollectionAssert.AreEqual(new[] { 0 }, TrailRoute.Order(one, true, 0));
        CollectionAssert.AreEqual(new[] { 0 }, TrailRoute.Order(one, false, 0));
    }

    [Test]
    public void NoStepsStartsAtTheFirstStop()
    {
        var stops = new List<TrailRoute.Stop>
        {
            S(42.0, 26.0, 0), S(42.0, 26.1, 0), S(42.0, 26.2, 0)
        };

        Assert.AreEqual(0, TrailRoute.StartIndex(stops, true));
        Assert.AreEqual(0, TrailRoute.StartIndex(stops, false));
        // Ordered with no steps: the order given.
        CollectionAssert.AreEqual(new[] { 0, 1, 2 }, TrailRoute.Order(stops, true, 0));
    }

    [Test]
    public void NoStopsHasNoStart()
    {
        Assert.AreEqual(-1, TrailRoute.StartIndex(new List<TrailRoute.Stop>(), true));
        CollectionAssert.IsEmpty(TrailRoute.Order(new List<TrailRoute.Stop>(), false, -1));
    }
}
