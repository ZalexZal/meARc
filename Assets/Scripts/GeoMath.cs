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

    /// <summary>
    /// Initial bearing from A to B, in degrees clockwise from true north.
    ///
    /// The map is drawn north-up, so this is the angle a pin sits at around the
    /// reader and the angle an arrow points. It is the initial bearing on a great
    /// circle, which over the distances this app works at is the bearing.
    /// </summary>
    public static double BearingDegrees(double latitudeA, double longitudeA,
                                        double latitudeB, double longitudeB)
    {
        double latA = latitudeA * DegreesToRadians;
        double latB = latitudeB * DegreesToRadians;
        double deltaLon = (longitudeB - longitudeA) * DegreesToRadians;

        double y = Math.Sin(deltaLon) * Math.Cos(latB);
        double x = Math.Cos(latA) * Math.Sin(latB) -
                   Math.Sin(latA) * Math.Cos(latB) * Math.Cos(deltaLon);

        double degrees = Math.Atan2(y, x) / DegreesToRadians;
        return (degrees + 360d) % 360d;
    }

    /// <summary>
    /// How far B lies east and north of A, in metres.
    ///
    /// This is the flat-earth approximation, which is what a small map wants: it
    /// keeps a straight line straight and costs two trig calls instead of a
    /// projection. Over a few kilometres the error is centimetres.
    /// </summary>
    public static void LocalOffsetMetres(double latitudeA, double longitudeA,
                                         double latitudeB, double longitudeB,
                                         out double east, out double north)
    {
        double metresPerDegreeLat = EarthRadiusMetres * DegreesToRadians;
        double metresPerDegreeLon = metresPerDegreeLat * Math.Cos(latitudeA * DegreesToRadians);

        east = (longitudeB - longitudeA) * metresPerDegreeLon;
        north = (latitudeB - latitudeA) * metresPerDegreeLat;
    }

    /// <summary>Inverse of <see cref="LocalOffsetMetres"/>.</summary>
    public static void OffsetToLatLon(double latitudeA, double longitudeA,
                                      double east, double north,
                                      out double latitude, out double longitude)
    {
        double metresPerDegreeLat = EarthRadiusMetres * DegreesToRadians;
        double metresPerDegreeLon = metresPerDegreeLat * Math.Cos(latitudeA * DegreesToRadians);

        latitude = latitudeA + north / metresPerDegreeLat;
        longitude = longitudeA + east / (metresPerDegreeLon < 1d ? 1d : metresPerDegreeLon);
    }
}
