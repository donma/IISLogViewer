using System.Text.Json;
using System.Text.Json.Serialization;
using IISLogExplorer.Core.Configuration;
using IISLogExplorer.Core.Models;

namespace IISLogExplorer.Infrastructure.Configuration;

public sealed class SettingsService : ISettingsService
{
    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "settings.json");
    public AppSettings Current { get; private set; }

    public SettingsService()
    {
        Current = Load();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        Current = settings;
        var json = JsonSerializer.Serialize(settings, Options());
        var temp = _path + ".tmp";
        await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);
        File.Move(temp, _path, true);
    }

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), Options()) ?? new AppSettings();
            }
        }
        catch
        {
        }

        return new AppSettings();
    }
}
