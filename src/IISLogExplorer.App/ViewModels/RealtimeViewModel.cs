using System.ComponentModel;
using System.Runtime.CompilerServices;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Realtime;
using IISLogExplorer.Core.Searching;

namespace IISLogExplorer.App.ViewModels;

public sealed class RealtimeViewModel : INotifyPropertyChanged
{
    private const int SeenCap = 100_000;
    private readonly IRealtimeMonitor _realtime;
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private LogSource? _source;
    private bool _isRealtime;

    public RealtimeViewModel(IRealtimeMonitor realtime)
    {
        _realtime = realtime;
        _realtime.EntriesAdded += OnEntriesAdded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<LogEntry>? EntryReceived;

    public LogSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            OnPropertyChanged();
        }
    }

    public bool IsRealtime { get => _isRealtime; private set { _isRealtime = value; OnPropertyChanged(); } }

    public async Task StartAsync(LogSource source)
    {
        _seen.Clear();
        _source = source;
        await _realtime.StartAsync(source).ConfigureAwait(true);
        IsRealtime = true;
    }

    public async Task StopAsync()
    {
        await _realtime.StopAsync().ConfigureAwait(true);
        IsRealtime = false;
    }

    private void OnEntriesAdded(object? sender, IReadOnlyList<LogEntry> entries)
    {
        RunOnUi(() =>
        {
            var request = _source is null ? null : BuildRequest();
            foreach (var entry in entries)
            {
                if (request is not null && !SearchPredicate.Matches(entry, request)) continue;
                var key = $"{entry.LineNumber}|{entry.RawLine}";
                if (!_seen.Add(key)) continue;
                if (_seen.Count > SeenCap)
                {
                    _seen.Clear();
                }

                EntryReceived?.Invoke(entry);
            }
        });
    }

    private SearchRequest BuildRequest()
    {
        return new SearchRequest { Source = _source! };
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        try
        {
            dispatcher.BeginInvoke(action);
        }
        catch (TaskCanceledException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}