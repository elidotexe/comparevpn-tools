namespace CompareVPN.Data.Networks;

/// <summary>What happened when an address was looked up in the list of networks.</summary>
public enum AsnSearchOutcome
{
    /// <summary>The address is inside a block of addresses in the list.</summary>
    Found,

    /// <summary>The list is loaded, but no block in it contains the address.</summary>
    NotInDataset,

    /// <summary>No list is loaded yet, for example because the file hasn't been downloaded.</summary>
    DataNotAvailable,
}
