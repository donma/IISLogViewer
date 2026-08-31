using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Core.Configuration;

public interface ISettingsService
{
    AppSettings Current { get; }
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
