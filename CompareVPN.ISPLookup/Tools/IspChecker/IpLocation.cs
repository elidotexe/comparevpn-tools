namespace CompareVPN.Tools.IspChecker;

/// <summary>
/// Roughly where an address is, ready to show to a visitor. This is an estimate from a downloaded list: the country is
/// usually right, the city is often the provider's nearest hub, and the coordinates are the middle of that city, never
/// a street address. Any part the list doesn't know is <c>null</c>.
/// </summary>
/// <param name="City">City name in English, e.g. "Mountain View".</param>
/// <param name="Region">State, province or region in English, e.g. "California".</param>
/// <param name="CountryCode">Two-letter country code, e.g. "US".</param>
/// <param name="Latitude">Approximate latitude of the city, rounded to 2 decimals (about 1 km).</param>
/// <param name="Longitude">Approximate longitude of the city, rounded to 2 decimals (about 1 km).</param>
public sealed record IpLocation(string? City, string? Region, string? CountryCode, double? Latitude, double? Longitude);
