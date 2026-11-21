using CompareVPN.Data;

namespace CompareVPN.Data.Networks;

/// <summary>A data source that reads its list of networks from a file which can be replaced while the website runs.</summary>
public interface IReloadableAsnDataSource : IAsnDataSource, IReloadableDataFile;
