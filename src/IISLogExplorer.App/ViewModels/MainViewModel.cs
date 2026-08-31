using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using IISLogExplorer.App.Commands;
using IISLogExplorer.App.Diagnostics;
using IISLogExplorer.Core.Analysis;
using IISLogExplorer.Core.Configuration;
using IISLogExplorer.Core.Exporting;
using IISLogExplorer.Core.IIS;
using IISLogExplorer.Core.Indexing;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Realtime;
using IISLogExplorer.Core.Searching;
using IISLogExplorer.Core.Security;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Logging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace IISLogExplorer.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IIndexService _index;
    private readonly IIisDiscoveryService _iis;
    private readonly SourceRepository _sources;
    private readonly ISettingsService _settings;
    private readonly IIpAnalyzer _ipAnalyzer;
    private readonly IErrorAnalyzer _errorAnalyzer;
    private readonly ISlowRequestAnalyzer _slowAnalyzer;
    private readonly ITrafficAnalyzer _trafficAnalyzer;
    private readonly ISecurityAnalyzer _securityAnalyzer;
    private readonly IExportService _export;
    private readonly LogEntryRepository _entries;
    private readonly AppLogger _logger;
    private readonly SearchViewModel _searchVm;
    private readonly IndexViewModel _indexVm;
    private readonly RealtimeViewModel _realtimeVm;
    private CancellationTokenSource? _analysisCancellation;
    private LogSource? _selectedRecentSource;
    private IisSiteInfo? _selectedSite;
    private string _statusMessage = "尚未選擇 IIS Log";
    private string _activePage = "Search";
    private int _selectedTabIndex;
    private bool _isBusy;
    private LogSource? _source;
    private int _slowThreshold;
    private IpAnalysisResult? _ipResult;
    private ErrorAnalysisResult? _errorResult;
    private SlowRequestAnalysisResult? _slowResult;
    private TrafficAnalysisResult? _trafficResult;
    private SecurityAnalysisResult? _securityResult;
    private bool _includeSubfolders;
    private string _retention = "保留全部";
    private int _realtimeIntervalSeconds;
    private int _indexBatchSize;
    private int _pageSize;
    private string _clientIpHeaderPriorityText = string.Empty;

    public MainViewModel(ISearchService search, IIndexService index, IIndexCoordinator coordinator, IIisDiscoveryService iis, SourceRepository sources, ISettingsService settings, IIpAnalyzer ipAnalyzer, IErrorAnalyzer errorAnalyzer, ISlowRequestAnalyzer slowAnalyzer, ITrafficAnalyzer trafficAnalyzer, ISecurityAnalyzer securityAnalyzer, IExportService export, IRealtimeMonitor realtime, LogEntryRepository entries, AppLogger logger)
    {
        _index = index; _iis = iis; _sources = sources; _settings = settings; _ipAnalyzer = ipAnalyzer; _errorAnalyzer = errorAnalyzer; _slowAnalyzer = slowAnalyzer; _trafficAnalyzer = trafficAnalyzer; _securityAnalyzer = securityAnalyzer; _export = export; _entries = entries; _logger = logger;
        AsyncCommand.DefaultHandler ??= new AppErrorHandler(logger);
        _searchVm = new SearchViewModel(search, logger);
        _indexVm = new IndexViewModel(coordinator, index);
        _realtimeVm = new RealtimeViewModel(realtime);
        _realtimeVm.EntryReceived += OnRealtimeEntry;
        _searchVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SearchViewModel.StatusMessage)) StatusMessage = _searchVm.StatusMessage;
            if (args.PropertyName == nameof(SearchViewModel.IsBusy)) IsBusy = _searchVm.IsBusy;
        };
        _indexVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IndexViewModel.IndexMessage)) IndexMessage = _indexVm.IndexMessage;
            if (args.PropertyName == nameof(IndexViewModel.DbStatus)) DbStatus = _indexVm.DbStatus;
        };
        _realtimeVm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(RealtimeViewModel.IsRealtime)) IsRealtime = _realtimeVm.IsRealtime;
        };

        _slowThreshold = settings.Current.SlowRequestThresholdMs;
        _includeSubfolders = settings.Current.IncludeSubfolders;
        _realtimeIntervalSeconds = settings.Current.RealtimeRefreshIntervalSeconds;
        _indexBatchSize = Math.Clamp(settings.Current.IndexBatchSize, 100, 10000);
        _pageSize = settings.Current.DefaultPageSize;
        _clientIpHeaderPriorityText = settings.Current.ClientIpHeaderPriorityText;
        _retention = settings.Current.DatabaseRetentionDays switch { 30 => "30 天", 60 => "60 天", 90 => "90 天", 180 => "180 天", _ => "保留全部" };
        _searchVm.Source = Source;
        _searchVm.PageSize = settings.Current.DefaultPageSize;
        _searchVm.MaxSearchResults = Math.Clamp(settings.Current.MaxSearchResults, 100, 100000);
        RetentionOptions = ["保留全部", "30 天", "60 天", "90 天", "180 天"];
        RecentSources = new ObservableCollection<LogSource>();
        IisSites = new ObservableCollection<IisSiteInfo>();
        PageSizes = [100, 200, 500, 1000];
        QuickRanges = ["自訂", "最近 15 分鐘", "最近 1 小時", "今天", "昨天", "最近 24 小時", "最近 7 天"];
        Methods = ["", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT"];
        Results = _searchVm.Results;
        SearchCommand = new AsyncCommand(() => SearchAsync(), () => Source is not null && !IsBusy);
        StopSearchCommand = new RelayCommand(StopSearch, () => IsBusy);
        StopIndexCommand = new RelayCommand(StopIndex, () => _indexVm.IsRunning);
        SelectFolderCommand = new RelayCommand(SelectFolder);
        SelectFileCommand = new RelayCommand(SelectFile);
        DetectIisCommand = new AsyncCommand(DetectIisAsync, () => !IsBusy);
        UseSelectedSiteCommand = new RelayCommand(UseSelectedSite, () => SelectedSite is not null);
        AnalyzeIpCommand = new AsyncCommand(AnalyzeIpAsync, () => Source is not null && SelectedResult is not null);
        AnalyzeErrorsCommand = new AsyncCommand(AnalyzeErrorsAsync, () => Source is not null);
        AnalyzeSlowCommand = new AsyncCommand(AnalyzeSlowAsync, () => Source is not null);
        AnalyzeTrafficCommand = new AsyncCommand(AnalyzeTrafficAsync, () => Source is not null);
        StartSecurityAnalysisCommand = new AsyncCommand(AnalyzeSecurityAsync, () => Source is not null);
        AnalyzeEntireSourceCommand = new AsyncCommand(AnalyzeSecurityEntireSourceAsync, () => Source is not null);
        StopAnalysisCommand = new RelayCommand(StopAnalysis, () => IsBusy);
        UseRecentSourceCommand = new RelayCommand(UseRecentSource, () => SelectedRecentSource is not null);
        StartRealtimeCommand = new AsyncCommand(StartRealtimeAsync, () => Source is not null && !IsRealtime);
        StopRealtimeCommand = new AsyncCommand(StopRealtimeAsync, () => IsRealtime);
        ExportCsvCommand = new AsyncCommand(() => ExportAsync(false), () => Results.Count > 0);
        ExportJsonCommand = new AsyncCommand(() => ExportAsync(true), () => Results.Count > 0);
        ClearIndexCommand = new AsyncCommand(ClearIndexAsync, () => !IsBusy);
        RebuildIndexCommand = new AsyncCommand(RebuildIndexAsync, () => Source is not null && !IsBusy);
        OptimizeCommand = new AsyncCommand(OptimizeAsync, () => !IsBusy && !_indexVm.IsRunning);
        CleanupNowCommand = new AsyncCommand(CleanupNowAsync, () => !IsBusy);
        SaveSettingsCommand = new AsyncCommand(SaveSettingsAsync, () => !IsBusy);
        StartIndexCommand = new AsyncCommand(StartIndexAsync, () => Source is not null && !IsBusy && !_indexVm.IsRunning);
        BrowseRecentCommand = new RelayCommand(() => ActivePage = "Search");
        _ = LoadRecentSourcesAsync();
        DbStatus = "DB —";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<SearchResult> Results { get; }
    public ObservableCollection<LogSource> RecentSources { get; }
    public ObservableCollection<IisSiteInfo> IisSites { get; }
    public IReadOnlyList<int> PageSizes { get; }
    public IReadOnlyList<string> QuickRanges { get; }
    public IReadOnlyList<string> Methods { get; }
    public IReadOnlyList<string> RetentionOptions { get; }
    public ICommand SearchCommand { get; }
    public ICommand StopSearchCommand { get; }
    public ICommand StopIndexCommand { get; }
    public ICommand StopAnalysisCommand { get; }
    public ICommand AnalyzeEntireSourceCommand { get; }
    public ICommand UseRecentSourceCommand { get; }
    public ICommand SelectFolderCommand { get; }
    public ICommand SelectFileCommand { get; }
    public ICommand DetectIisCommand { get; }
    public ICommand UseSelectedSiteCommand { get; }
    public ICommand AnalyzeIpCommand { get; }
    public ICommand AnalyzeErrorsCommand { get; }
    public ICommand AnalyzeSlowCommand { get; }
    public ICommand AnalyzeTrafficCommand { get; }
    public ICommand StartSecurityAnalysisCommand { get; }
    public ICommand StartRealtimeCommand { get; }
    public ICommand StopRealtimeCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportJsonCommand { get; }
    public ICommand ClearIndexCommand { get; }
    public ICommand RebuildIndexCommand { get; }
    public ICommand OptimizeCommand { get; }
    public ICommand CleanupNowCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand StartIndexCommand { get; }
    public ICommand BrowseRecentCommand { get; }

    public LogSource? Source { get => _source; private set { _source = value; _searchVm.Source = value; _realtimeVm.Source = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourceName)); RaiseCommands(); } }
    public string SourceName => Source?.DisplayName ?? "未選擇來源";
    public string Keyword { get => _searchVm.Keyword; set => _searchVm.Keyword = value; }
    public string UrlFilter { get => _searchVm.UrlFilter; set => _searchVm.UrlFilter = value; }
    public string IpFilter { get => _searchVm.IpFilter; set => _searchVm.IpFilter = value; }
    public string UserAgentFilter { get => _searchVm.UserAgentFilter; set => _searchVm.UserAgentFilter = value; }
    public string UsernameFilter { get => _searchVm.UsernameFilter; set => _searchVm.UsernameFilter = value; }
    public string MethodFilter { get => _searchVm.MethodFilter; set => _searchVm.MethodFilter = value; }
    public string StatusFilter { get => _searchVm.StatusFilter; set => _searchVm.StatusFilter = value; }
    public string MinTime { get => _searchVm.MinTime; set => _searchVm.MinTime = value; }
    public string MaxTime { get => _searchVm.MaxTime; set => _searchVm.MaxTime = value; }
    public DateTime? DateFrom { get => _searchVm.DateFrom; set => _searchVm.DateFrom = value; }
    public DateTime? DateTo { get => _searchVm.DateTo; set => _searchVm.DateTo = value; }
    public string TimeFrom { get => _searchVm.TimeFrom; set => _searchVm.TimeFrom = value; }
    public string TimeTo { get => _searchVm.TimeTo; set => _searchVm.TimeTo = value; }
    public string QuickRange { get => _searchVm.QuickRange; set => _searchVm.QuickRange = value; }
    public bool IncludeSubfolders { get => _includeSubfolders; set { _includeSubfolders = value; OnPropertyChanged(); } }
    public string Retention { get => _retention; set { _retention = value; OnPropertyChanged(); } }
    public int RealtimeIntervalSeconds { get => _realtimeIntervalSeconds; set { _realtimeIntervalSeconds = Math.Max(1, value); OnPropertyChanged(); } }
    public int MaxSearchResults { get => _searchVm.MaxSearchResults; set { _searchVm.MaxSearchResults = value; OnPropertyChanged(); } }
    public int IndexBatchSize { get => _indexBatchSize; set { _indexBatchSize = Math.Clamp(value, 100, 10000); OnPropertyChanged(); } }
    public string ClientIpHeaderPriorityText { get => _clientIpHeaderPriorityText; set { _clientIpHeaderPriorityText = value; OnPropertyChanged(); } }
    public LogSource? SelectedRecentSource { get => _selectedRecentSource; set { _selectedRecentSource = value; OnPropertyChanged(); RaiseCommands(); } }
    public IisSiteInfo? SelectedSite { get => _selectedSite; set { _selectedSite = value; OnPropertyChanged(); RaiseCommands(); } }
    public int PageSize { get => _searchVm.PageSize; set { _searchVm.PageSize = value; OnPropertyChanged(); } }
    public int SlowThreshold { get => _slowThreshold; set { _slowThreshold = value; OnPropertyChanged(); } }
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
    public string IndexMessage { get => _indexVm.IndexMessage; private set { OnPropertyChanged(); } }
    public string DbStatus { get => _indexVm.DbStatus; private set { OnPropertyChanged(); } }
    public string ActivePage { get => _activePage; set { _activePage = value; _selectedTabIndex = value switch { "IP Analysis" => 1, "Errors" => 2, "Security" => 3, "Slow Requests" => 4, "Traffic" => 5, "Settings" => 6, _ => 0 }; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedTabIndex)); } }
    public int SelectedTabIndex { get => _selectedTabIndex; set { _selectedTabIndex = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); RaiseCommands(); } }
    public bool IsRealtime { get => _realtimeVm.IsRealtime; private set { OnPropertyChanged(); } }
    public SearchResult? SelectedResult { get => _searchVm.SelectedResult; set => _searchVm.SelectedResult = value; }
    public IpAnalysisResult? IpResult { get => _ipResult; private set { _ipResult = value; OnPropertyChanged(); } }
    public ErrorAnalysisResult? ErrorResult { get => _errorResult; private set { _errorResult = value; OnPropertyChanged(); } }
    public SlowRequestAnalysisResult? SlowResult { get => _slowResult; private set { _slowResult = value; OnPropertyChanged(); } }
    public TrafficAnalysisResult? TrafficResult { get => _trafficResult; private set { _trafficResult = value; OnPropertyChanged(); } }
    public SecurityAnalysisResult? SecurityResult { get => _securityResult; private set { _securityResult = value; OnPropertyChanged(); } }

    private async Task SearchAsync()
    {
        await _searchVm.SearchAsync(onStarted: _ => _indexVm.StartIndex(_searchVm.BuildRequest()), onBusyChanged: RaiseCommands, onStatusChanged: message => StatusMessage = message).ConfigureAwait(true);
    }

    private void StopSearch() => _searchVm.StopSearch();

    private void StopIndex() => _ = _indexVm.StopIndexAsync();

    private async Task StartIndexAsync()
    {
        if (Source is null) return;
        _indexVm.StartIndex(_searchVm.BuildRequest());
        await Task.CompletedTask;
    }

    private void SelectFolder()
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "選擇 IIS Log 資料夾", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) _ = SetSourceAsync(new LogSource { SourceType = LogSourceType.Folder, DisplayName = Path.GetFileName(dialog.SelectedPath.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : dialog.SelectedPath, Path = dialog.SelectedPath, IncludeSubfolders = IncludeSubfolders });
    }

    private void SelectFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "IIS Log (*.log)|*.log|All files (*.*)|*.*", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog() == true) _ = SetSourceAsync(new LogSource { SourceType = LogSourceType.File, DisplayName = Path.GetFileName(dialog.FileName), Path = dialog.FileName });
    }

    private async Task DetectIisAsync()
    {
        try { IisSites.Clear(); foreach (var site in await _iis.DiscoverSitesAsync()) IisSites.Add(site); StatusMessage = IisSites.Count == 0 ? "找不到可用 IIS 站台或 IIS 未安裝" : $"偵測到 {IisSites.Count} 個 IIS 站台"; }
        catch (Exception exception) { StatusMessage = $"IIS 偵測不可用：{exception.Message}"; }
    }

    private void UseSelectedSite()
    {
        if (SelectedSite is null) return;
        _ = SetSourceAsync(new LogSource { SourceType = LogSourceType.IisSite, DisplayName = SelectedSite.Name, Path = SelectedSite.LogDirectory });
    }

    private async Task SetSourceAsync(LogSource source)
    {
        Source = await _sources.SaveAsync(source);
        StatusMessage = $"已選擇：{Source.DisplayName}；尚未搜尋";
        await _logger.LogAsync($"Source selected: {Source.SourceType} {Source.Path}").ConfigureAwait(true);
        await LoadRecentSourcesAsync();
    }

    private async Task LoadRecentSourcesAsync()
    {
        try { RecentSources.Clear(); foreach (var source in await _sources.GetRecentAsync()) RecentSources.Add(source); } catch { }
    }

    private async Task AnalyzeIpAsync()
    {
        if (Source is null || SelectedResult is null) return;
        var ip = SelectedResult.Entry.ResolvedClientIp ?? SelectedResult.Entry.ClientIp;
        if (string.IsNullOrWhiteSpace(ip)) return;
        await RunAnalysisAsync(async token => IpResult = await _ipAnalyzer.AnalyzeAsync(_searchVm.GetEntriesForAnalysis(_searchVm.BuildRequest() with { ClientIp = ip }), ip, token), "IP 分析完成");
        ActivePage = "IP Analysis";
    }

    private async Task AnalyzeErrorsAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => ErrorResult = await _errorAnalyzer.AnalyzeAsync(_searchVm.GetEntriesForAnalysis(_searchVm.BuildRequest()), token), "錯誤分析完成");
        ActivePage = "Errors";
    }

    private async Task AnalyzeSlowAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => SlowResult = await _slowAnalyzer.AnalyzeAsync(_searchVm.GetEntriesForAnalysis(_searchVm.BuildRequest()), SlowThreshold, token), "慢請求分析完成");
        ActivePage = "Slow Requests";
    }

    private async Task AnalyzeTrafficAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => TrafficResult = await _trafficAnalyzer.AnalyzeAsync(_searchVm.GetEntriesForAnalysis(_searchVm.BuildRequest()), token), "流量分析完成");
        ActivePage = "Traffic";
    }

    private async Task AnalyzeSecurityAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => SecurityResult = await _securityAnalyzer.AnalyzeAsync(_searchVm.GetEntriesForAnalysis(_searchVm.BuildRequest()), token), "資安分析完成");
        ActivePage = "Security";
    }

    private async Task AnalyzeSecurityEntireSourceAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => SecurityResult = await _securityAnalyzer.AnalyzeAsync(_searchVm.GetEntriesForAnalysis(new SearchRequest { Source = Source!, MaxResults = int.MaxValue }), token), "資安分析完成（整個來源）");
        ActivePage = "Security";
    }

    private void StopAnalysis() => _analysisCancellation?.Cancel();

    private void UseRecentSource()
    {
        if (SelectedRecentSource is null) return;
        _ = SetSourceAsync(SelectedRecentSource);
    }

    private void OnRealtimeEntry(LogEntry entry)
    {
        _searchVm.InsertRealtime(entry);
    }

    private async Task StartRealtimeAsync()
    {
        if (Source is null) return;
        await _realtimeVm.StartAsync(Source).ConfigureAwait(true);
        StatusMessage = "Realtime Monitor ON";
    }

    private async Task StopRealtimeAsync()
    {
        await _realtimeVm.StopAsync().ConfigureAwait(true);
        StatusMessage = "Realtime Monitor OFF";
    }

    private async Task RunAnalysisAsync(Func<CancellationToken, Task> action, string completedMessage)
    {
        _analysisCancellation?.Cancel(); _analysisCancellation = new CancellationTokenSource(); IsBusy = true; StatusMessage = "分析中...";
        try { await action(_analysisCancellation.Token); StatusMessage = completedMessage; } catch (OperationCanceledException) { StatusMessage = "分析已停止"; } catch (Exception exception) { StatusMessage = $"分析失敗：{exception.Message}"; await _logger.LogAsync("Analysis failed", exception).ConfigureAwait(true); } finally { IsBusy = false; }
    }

    private async Task RefreshDbStatusAsync() => await _indexVm.RefreshDbStatusAsync().ConfigureAwait(false);

    private async Task CleanupNowAsync()
    {
        if (System.Windows.MessageBox.Show("依 Retention 設定清理 SQLite 中過期 Request？原始 IIS Log 不會被修改。", "確認", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        var days = Retention switch { "30 天" => 30, "60 天" => 60, "90 天" => 90, "180 天" => 180, _ => (int?)null };
        var removed = await _index.CleanupAsync(days);
        StatusMessage = removed > 0 ? $"已清理 {removed:N0} 筆過期資料" : "沒有符合條件的過期資料";
        await RefreshDbStatusAsync();
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

    private async Task ExportAsync(bool json)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = json ? "JSON (*.json)|*.json" : "CSV (*.csv)|*.csv", FileName = $"iis-log-export-{DateTime.Now:yyyyMMdd-HHmmss}" + (json ? ".json" : ".csv") };
        if (dialog.ShowDialog() != true) return;
        if (Results.Count >= 10000 && System.Windows.MessageBox.Show($"將依目前篩選重新執行搜尋並匯出（可能大量）。是否繼續？", "匯出確認", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;
        IsBusy = true;
        using var exportCancellation = new CancellationTokenSource();
        try { var request = _searchVm.BuildRequest(); if (json) await _export.ExportJsonAsync(_searchVm.SearchResults(request, exportCancellation.Token), dialog.FileName, exportCancellation.Token); else await _export.ExportCsvAsync(_searchVm.SearchResults(request, exportCancellation.Token), dialog.FileName, exportCancellation.Token); StatusMessage = "匯出完成"; } catch (OperationCanceledException) { StatusMessage = "匯出已取消"; } catch (Exception exception) { StatusMessage = $"匯出失敗：{exception.Message}"; await _logger.LogAsync("Export failed", exception).ConfigureAwait(true); } finally { IsBusy = false; }
    }

    private async Task OptimizeAsync()
    {
        if (System.Windows.MessageBox.Show("執行 SQLite OPTIMIZE + VACUUM？大型資料庫可能需較長時間，且會與背景索引互斥。", "確認", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        IsBusy = true;
        try { await _index.OptimizeAsync(); StatusMessage = "SQLite 最佳化完成"; } catch (Exception exception) { StatusMessage = $"最佳化失敗：{exception.Message}"; await _logger.LogAsync("Optimize failed", exception).ConfigureAwait(true); } finally { IsBusy = false; }
    }

    private async Task ClearIndexAsync()
    {
        if (System.Windows.MessageBox.Show("確定清除 SQLite 索引？原始 IIS Log 不會被修改。", "確認", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        await _index.ClearAsync(); _indexVm.SetMessage("索引已清除");
    }

    private async Task RebuildIndexAsync()
    {
        if (Source is null) return;
        IsBusy = true; try { await _index.RebuildAsync(Source); _indexVm.SetMessage("索引重建完成"); } catch (Exception exception) { _indexVm.SetMessage($"重建失敗：{exception.Message}"); await _logger.LogAsync("Rebuild failed", exception).ConfigureAwait(true); } finally { IsBusy = false; }
    }

    private async Task SaveSettingsAsync()
    {
        var retentionDays = Retention switch
        {
            "30 天" => 30,
            "60 天" => 60,
            "90 天" => 90,
            "180 天" => 180,
            _ => (int?)null
        };
        var priority = ClientIpHeaderPriorityText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await _settings.SaveAsync(_settings.Current with { DefaultPageSize = PageSize, IndexBatchSize = IndexBatchSize, MaxSearchResults = MaxSearchResults, SlowRequestThresholdMs = SlowThreshold, IncludeSubfolders = IncludeSubfolders, DatabaseRetentionDays = retentionDays, RealtimeRefreshIntervalSeconds = Math.Max(1, RealtimeIntervalSeconds), ClientIpHeaderPriorityText = ClientIpHeaderPriorityText, ClientIpHeaderPriority = priority }); StatusMessage = "設定已儲存";
    }

    private void RaiseCommands()
    {
        foreach (var command in new[] { SearchCommand, StopSearchCommand, StopIndexCommand, StopAnalysisCommand, DetectIisCommand, UseSelectedSiteCommand, UseRecentSourceCommand, AnalyzeIpCommand, AnalyzeErrorsCommand, AnalyzeSlowCommand, AnalyzeTrafficCommand, StartSecurityAnalysisCommand, AnalyzeEntireSourceCommand, StartRealtimeCommand, StopRealtimeCommand, ExportCsvCommand, ExportJsonCommand, ClearIndexCommand, RebuildIndexCommand, OptimizeCommand, SaveSettingsCommand, StartIndexCommand }) (command as AsyncCommand)?.RaiseCanExecuteChanged();
        foreach (var command in new[] { StopSearchCommand, StopIndexCommand, StopAnalysisCommand, UseSelectedSiteCommand, UseRecentSourceCommand }) (command as RelayCommand)?.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}