namespace IISLogExplorer.Core.IIS;

public sealed record IisSiteInfo(string Name, string Id, string Bindings, string LogDirectory, bool Enabled)
{
    public override string ToString() => $"{Name} (Site ID: {Id}) — {LogDirectory}";
}

public interface IIisDiscoveryService
{
    Task<IReadOnlyList<IisSiteInfo>> DiscoverSitesAsync(CancellationToken cancellationToken = default);
}
