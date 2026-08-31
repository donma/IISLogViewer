using System.Xml.Linq;
using IISLogExplorer.Core.IIS;

namespace IISLogExplorer.Infrastructure.IIS;

public sealed class IisDiscoveryService : IIisDiscoveryService
{
    public Task<IReadOnlyList<IisSiteInfo>> DiscoverSitesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<IisSiteInfo>>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "config", "applicationHost.config");
            if (!File.Exists(path)) return [];
            try
            {
                var document = XDocument.Load(path, LoadOptions.None);
                var sites = document.Descendants("site").Select(site =>
                {
                    var name = (string?)site.Attribute("name") ?? "Unnamed";
                    var id = (string?)site.Attribute("id") ?? string.Empty;
                    var bindings = string.Join("; ", site.Element("bindings")?.Elements("binding").Select(x => (string?)x.Attribute("bindingInformation") ?? string.Empty) ?? []);
                    var logDirectory = (string?)site.Descendants("logFile").FirstOrDefault()?.Attribute("directory") ?? @"%SystemDrive%\inetpub\logs\LogFiles";
                    logDirectory = Environment.ExpandEnvironmentVariables(logDirectory);
                    var siteId = string.IsNullOrWhiteSpace(id) ? "" : $"W3SVC{id}";
                    var full = Directory.Exists(logDirectory) && !string.IsNullOrWhiteSpace(siteId) ? Path.Combine(logDirectory, siteId) : logDirectory;
                    var autoStart = (string?)site.Attribute("serverAutoStart");
                    var enabled = !string.Equals(autoStart, "false", StringComparison.OrdinalIgnoreCase);
                    return new IisSiteInfo(name, id, bindings, full, enabled);
                }).ToArray();
                return sites;
            }
            catch
            {
                return [];
            }
        }, cancellationToken);
    }
}
