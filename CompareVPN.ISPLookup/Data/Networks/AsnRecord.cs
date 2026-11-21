namespace CompareVPN.Data.Networks;

/// <summary>An autonomous system (network operator) that announces a block of IP addresses.</summary>
/// <param name="Asn">Autonomous system number, e.g. 13335.</param>
/// <param name="Organization">Operator name as published in the dataset, e.g. "CLOUDFLARENET".</param>
/// <param name="CountryCode">ISO 3166-1 alpha-2 country code of the registration, if known.</param>
public sealed record AsnRecord(int Asn, string Organization, string? CountryCode);
