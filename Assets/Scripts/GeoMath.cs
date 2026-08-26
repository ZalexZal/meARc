using System;

/// <summary>
/// Great-circle distance helpers. Kept in double precision throughout, because
/// building coordinates carry ~10 decimal places and float would lose metres.
/// </summary>
public static class GeoMath
{
    const double EarthRadiusMetres = 6371000d;
    const double DegreesToRadians = Math.PI / 180d;

    /// <summary>
    /// Horizontal distance in metres between two lat/long pairs, ignoring altitude.
    /// </summary>
    public static double DistanceMetres(double latitudeA, double longitudeA,
                                        double latitudeB, double longitudeB)
    {
        double deltaLat = (latitudeB - latitudeA) * DegreesToRadians;
        double deltaLon = (longitudeB - longitudeA) * DegreesToRadians;

        double sinLat = Math.Sin(deltaLat / 2d);
        double sinLon = Math.Sin(deltaLon / 2d);

        double a = sinLat * sinLat +
                   Math.Cos(latitudeA * DegreesToRadians) *
                   Math.Cos(latitudeB * DegreesToRadians) *
                   sinLon * sinLon;

        return EarthRadiusMetres * 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
    }
}
