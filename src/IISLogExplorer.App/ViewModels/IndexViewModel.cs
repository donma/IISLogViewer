using System.ComponentModel;
using System.Runtime.CompilerServices;
using IISLogExplorer.Core.Indexing;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Searching;

namespace IISLogExplorer.App.ViewModels;

public sealed class IndexViewModel : INotifyPropertyChanged
{
    private readonly IIndexCoordinator _coordinator;
    private readonly IIndexService _index;
    private string _indexMessage = "未建立索引";
    private string _dbStatus = "DB —";

    public IndexViewModel(IIndexCoordinator coordinator, IIndexService index)
    {
        _coordinator = coordinator;
        _index = index;
        _index.ProgressChanged += OnIndexProgress;
        _coordinator.StateChanged += (_, state) => RunOnUi(() =>
        {
            if (state.IsRunning) IndexMessage = $"Index 進行中：{state.Source?.DisplayName ?? ""}";
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsRunning => _coordinator.IsRunning;
    public string IndexMessage { get => _indexMessage; private set { _indexMessage = value; OnPropertyChanged(); } }
    public string DbStatus { get => _dbStatus; private set { _dbStatus = value; OnPropertyChanged(); } }

    public void SetMessage(string message) => RunOnUi(() => IndexMessage = message);

    public void StartIndex(SearchRequest request)
    {
        _coordinator.Enqueue(request.Source, request);
        RunOnUi(() => IndexMessage = "Index 排入佇列");
    }

    public Task StopIndexAsync(CancellationToken cancellationToken = default) => _coordinator.CancelAsync(cancellationToken);

    public async Task RefreshDbStatusAsync()
    {
        try
        {
            var stats = await _index.GetStatsAsync().ConfigureAwait(false);
            RunOnUi(() => DbStatus = $"DB {stats.SizeBytes / 1024d / 1024d:0.0} MB · {stats.IndexedRecords:N0} records · {stats.IndexedFiles} files");
        }
        catch
        {
        }
    }

    private void OnIndexProgress(object? sender, IndexProgress progress)
    {
        RunOnUi(() =>
        {
            IndexMessage = progress.IsRunning ? $"Index {progress.Percentage:0}% · {progress.IndexedRecords:N0} records · {progress.FileName}" : "Index 完成";
            if (!progress.IsRunning)
            {
                _ = RefreshDbStatusAsync();
            }
        });
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