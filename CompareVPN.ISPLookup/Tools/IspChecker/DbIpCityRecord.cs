using MaxMind.Db;

namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// The parts of one entry in the DB-IP location file that the ISP checker uses, named as they are in the file. The
/// file holds more (continent, names in other languages); those are skipped. MaxMind's GeoLite2 City file uses the
/// same names, so it can be read the same way.
/// </summary>
/// <remarks>
/// The file reader builds these through the constructor marked <c>[Constructor]</c>, matching each
/// <c>[MapKey]</c> to a name in the file. C# can't put that mark on a primary constructor, so these classes use
/// ordinary ones.
/// </remarks>
internal sealed class DbIpCityRecord
{
    [Constructor]
    public DbIpCityRecord(
        [MapKey("city")] Named? city,
        [MapKey("subdivisions")] List<Named>? subdivisions,
        [MapKey("country")] Country? country,
        [MapKey("location")] Coordinates? location)
    {
        City = city?.English;
        Region = subdivisions?.FirstOrDefault()?.English; // The file lists the largest region (state, province) first.
        CountryCode = country?.IsoCode;
        Latitude = location?.Latitude;
        Longitude = location?.Longitude;
    }

    public string? City { get; }

    public string? Region { get; }

    public string? CountryCode { get; }

    public double? Latitude { get; }

    public double? Longitude { get; }

    /// <summary>A place with names in several languages.</summary>
    internal sealed class Named
    {
        [Constructor]
        public Named([MapKey("names")] Dictionary<string, string>? names) => English = names?.GetValueOrDefault("en");

        public string? English { get; }
    }

    /// <summary>A country, by its two-letter code.</summary>
    internal sealed class Country
    {
        [Constructor]
        public Country([MapKey("iso_code")] string? isoCode) => IsoCode = isoCode;

        public string? IsoCode { get; }
    }

    /// <summary>The middle of the city (or of the country, when the city isn't known).</summary>
    internal sealed class Coordinates
    {
        [Constructor]
        public Coordinates([MapKey("latitude")] double? latitude, [MapKey("longitude")] double? longitude)
        {
            Latitude = latitude;
            Longitude = longitude;
        }

        public double? Latitude { get; }

        public double? Longitude { get; }
    }
}
