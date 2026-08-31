using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using IISLogExplorer.App.Commands;
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
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace IISLogExplorer.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ISearchService _search;
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
    private readonly IRealtimeMonitor _realtime;
    private readonly LogEntryRepository _entries;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _analysisCancellation;
    private CancellationTokenSource? _indexCancellation;
    private int _indexRunning;
    private readonly HashSet<string> _realtimeSeen = new(StringComparer.OrdinalIgnoreCase);
    private string _keyword = string.Empty;
    private string _urlFilter = string.Empty;
    private string _ipFilter = string.Empty;
    private string _userAgentFilter = string.Empty;
    private string _usernameFilter = string.Empty;
    private string _methodFilter = string.Empty;
    private string _statusFilter = string.Empty;
    private string _minTime = string.Empty;
    private string _maxTime = string.Empty;
    private DateTime? _dateFrom;
    private DateTime? _dateTo;
    private string _timeFrom = string.Empty;
    private string _timeTo = string.Empty;
    private string _quickRange = "自訂";
    private bool _includeSubfolders;
    private string _retention = "保留全部";
    private int _realtimeIntervalSeconds;
    private int _maxSearchResults;
    private int _indexBatchSize;
    private string _clientIpHeaderPriorityText = string.Empty;
    private LogSource? _selectedRecentSource;
    private IisSiteInfo? _selectedSite;
    private string _dbStatus = string.Empty;
    private string _statusMessage = "尚未選擇 IIS Log";
    private string _indexMessage = "未建立索引";
    private string _activePage = "Search";
    private int _selectedTabIndex;
    private bool _isBusy;
    private bool _isRealtime;
    private LogSource? _source;
    private SearchResult? _selectedResult;
    private int _pageSize;
    private int _slowThreshold;
    private IpAnalysisResult? _ipResult;
    private ErrorAnalysisResult? _errorResult;
    private SlowRequestAnalysisResult? _slowResult;
    private TrafficAnalysisResult? _trafficResult;
    private SecurityAnalysisResult? _securityResult;

    public MainViewModel(ISearchService search, IIndexService index, IIisDiscoveryService iis, SourceRepository sources, ISettingsService settings, IIpAnalyzer ipAnalyzer, IErrorAnalyzer errorAnalyzer, ISlowRequestAnalyzer slowAnalyzer, ITrafficAnalyzer trafficAnalyzer, ISecurityAnalyzer securityAnalyzer, IExportService export, IRealtimeMonitor realtime, LogEntryRepository entries)
    {
        _search = search; _index = index; _iis = iis; _sources = sources; _settings = settings; _ipAnalyzer = ipAnalyzer; _errorAnalyzer = errorAnalyzer; _slowAnalyzer = slowAnalyzer; _trafficAnalyzer = trafficAnalyzer; _securityAnalyzer = securityAnalyzer; _export = export; _realtime = realtime; _entries = entries;
        _pageSize = settings.Current.DefaultPageSize;
        _slowThreshold = settings.Current.SlowRequestThresholdMs;
        _includeSubfolders = settings.Current.IncludeSubfolders;
        _realtimeIntervalSeconds = settings.Current.RealtimeRefreshIntervalSeconds;
        _maxSearchResults = Math.Clamp(settings.Current.MaxSearchResults, 100, 100000);
        _indexBatchSize = Math.Clamp(settings.Current.IndexBatchSize, 100, 10000);
        _clientIpHeaderPriorityText = settings.Current.ClientIpHeaderPriorityText;
        _retention = settings.Current.DatabaseRetentionDays switch { 30 => "30 天", 60 => "60 天", 90 => "90 天", 180 => "180 天", _ => "保留全部" };
        RetentionOptions = ["保留全部", "30 天", "60 天", "90 天", "180 天"];
        Results = new ObservableCollection<SearchResult>();
        RecentSources = new ObservableCollection<LogSource>();
        IisSites = new ObservableCollection<IisSiteInfo>();
        PageSizes = [100, 200, 500, 1000];
        QuickRanges = ["自訂", "最近 15 分鐘", "最近 1 小時", "今天", "昨天", "最近 24 小時", "最近 7 天"];
        Methods = ["", "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT"];
        SearchCommand = new AsyncCommand(SearchAsync, () => Source is not null && !IsBusy);
        StopSearchCommand = new RelayCommand(StopSearch, () => IsBusy);
        StopIndexCommand = new RelayCommand(StopIndex, () => Volatile.Read(ref _indexRunning) == 1);
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
        StartRealtimeCommand = new AsyncCommand(StartRealtimeAsync, () => Source is not null && !_isRealtime);
        StopRealtimeCommand = new AsyncCommand(StopRealtimeAsync, () => _isRealtime);
        ExportCsvCommand = new AsyncCommand(() => ExportAsync(false), () => Results.Count > 0);
        ExportJsonCommand = new AsyncCommand(() => ExportAsync(true), () => Results.Count > 0);
        ClearIndexCommand = new AsyncCommand(ClearIndexAsync, () => !IsBusy);
        RebuildIndexCommand = new AsyncCommand(RebuildIndexAsync, () => Source is not null && !IsBusy);
        OptimizeCommand = new AsyncCommand(() => _index.OptimizeAsync(), () => !IsBusy);
        CleanupNowCommand = new AsyncCommand(CleanupNowAsync, () => !IsBusy);
        SaveSettingsCommand = new AsyncCommand(SaveSettingsAsync, () => !IsBusy);
        StartIndexCommand = new AsyncCommand(() => StartIndexAsync(BuildRequest()), () => Source is not null && !IsBusy && Volatile.Read(ref _indexRunning) == 0);
        BrowseRecentCommand = new RelayCommand(() => ActivePage = "Search");
        _index.ProgressChanged += OnIndexProgress;
        _realtime.EntriesAdded += OnRealtimeEntriesAdded;
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
    public LogSource? Source { get => _source; private set { _source = value; OnPropertyChanged(); OnPropertyChanged(nameof(SourceName)); RaiseCommands(); } }
    public string SourceName => Source?.DisplayName ?? "未選擇來源";
    public string Keyword { get => _keyword; set { _keyword = value; OnPropertyChanged(); } }
    public string UrlFilter { get => _urlFilter; set { _urlFilter = value; OnPropertyChanged(); } }
    public string IpFilter { get => _ipFilter; set { _ipFilter = value; OnPropertyChanged(); } }
    public string UserAgentFilter { get => _userAgentFilter; set { _userAgentFilter = value; OnPropertyChanged(); } }
    public string UsernameFilter { get => _usernameFilter; set { _usernameFilter = value; OnPropertyChanged(); } }
    public string MethodFilter { get => _methodFilter; set { _methodFilter = value; OnPropertyChanged(); } }
    public string StatusFilter { get => _statusFilter; set { _statusFilter = value; OnPropertyChanged(); } }
    public string MinTime { get => _minTime; set { _minTime = value; OnPropertyChanged(); } }
    public string MaxTime { get => _maxTime; set { _maxTime = value; OnPropertyChanged(); } }
    public DateTime? DateFrom { get => _dateFrom; set { _dateFrom = value; QuickRange = "自訂"; OnPropertyChanged(); } }
    public DateTime? DateTo { get => _dateTo; set { _dateTo = value; QuickRange = "自訂"; OnPropertyChanged(); } }
    public string TimeFrom { get => _timeFrom; set { _timeFrom = value; OnPropertyChanged(); } }
    public string TimeTo { get => _timeTo; set { _timeTo = value; OnPropertyChanged(); } }
    public string QuickRange { get => _quickRange; set { _quickRange = value; OnPropertyChanged(); } }
    public bool IncludeSubfolders { get => _includeSubfolders; set { _includeSubfolders = value; OnPropertyChanged(); } }
    public string Retention { get => _retention; set { _retention = value; OnPropertyChanged(); } }
    public int RealtimeIntervalSeconds { get => _realtimeIntervalSeconds; set { _realtimeIntervalSeconds = Math.Max(1, value); OnPropertyChanged(); } }
    public int MaxSearchResults { get => _maxSearchResults; set { _maxSearchResults = Math.Clamp(value, 100, 100000); OnPropertyChanged(); } }
    public int IndexBatchSize { get => _indexBatchSize; set { _indexBatchSize = Math.Clamp(value, 100, 10000); OnPropertyChanged(); } }
    public string ClientIpHeaderPriorityText { get => _clientIpHeaderPriorityText; set { _clientIpHeaderPriorityText = value; OnPropertyChanged(); } }
    public LogSource? SelectedRecentSource { get => _selectedRecentSource; set { _selectedRecentSource = value; OnPropertyChanged(); RaiseCommands(); } }
    public IisSiteInfo? SelectedSite { get => _selectedSite; set { _selectedSite = value; OnPropertyChanged(); RaiseCommands(); } }
    public int PageSize { get => _pageSize; set { _pageSize = value; OnPropertyChanged(); } }
    public int SlowThreshold { get => _slowThreshold; set { _slowThreshold = value; OnPropertyChanged(); } }
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
    public string IndexMessage { get => _indexMessage; private set { _indexMessage = value; OnPropertyChanged(); } }
    public string DbStatus { get => _dbStatus; private set { _dbStatus = value; OnPropertyChanged(); } }
    public string ActivePage { get => _activePage; set { _activePage = value; _selectedTabIndex = value switch { "IP Analysis" => 1, "Errors" => 2, "Security" => 3, "Slow Requests" => 4, "Traffic" => 5, "Settings" => 6, _ => 0 }; OnPropertyChanged(); OnPropertyChanged(nameof(SelectedTabIndex)); } }
    public int SelectedTabIndex { get => _selectedTabIndex; set { _selectedTabIndex = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); RaiseCommands(); } }
    public bool IsRealtime { get => _isRealtime; private set { _isRealtime = value; OnPropertyChanged(); RaiseCommands(); } }
    public SearchResult? SelectedResult { get => _selectedResult; set { _selectedResult = value; OnPropertyChanged(); } }
    public IpAnalysisResult? IpResult { get => _ipResult; private set { _ipResult = value; OnPropertyChanged(); } }
    public ErrorAnalysisResult? ErrorResult { get => _errorResult; private set { _errorResult = value; OnPropertyChanged(); } }
    public SlowRequestAnalysisResult? SlowResult { get => _slowResult; private set { _slowResult = value; OnPropertyChanged(); } }
    public TrafficAnalysisResult? TrafficResult { get => _trafficResult; private set { _trafficResult = value; OnPropertyChanged(); } }
    public SecurityAnalysisResult? SecurityResult { get => _securityResult; private set { _securityResult = value; OnPropertyChanged(); } }

    private async Task SearchAsync()
    {
        if (Source is null) { StatusMessage = "尚未選擇 IIS Log 來源"; return; }
        _searchCancellation?.Cancel(); _searchCancellation?.Dispose(); _searchCancellation = new CancellationTokenSource();
        Results.Clear(); IsBusy = true; StatusMessage = "Searching...";
        try
        {
            var request = BuildRequest();
            _ = StartIndexAsync(request);
            await foreach (var result in _search.SearchAsync(request, _searchCancellation.Token).ConfigureAwait(true))
            {
                if (Results.Count < request.MaxResults) Results.Add(result);
                StatusMessage = $"搜尋 {Results.Count:N0} 筆";
            }
            if (Results.Count == 0) StatusMessage = "找不到符合條件的 Request";
            else StatusMessage = $"搜尋 {Results.Count:N0} 筆";
        }
        catch (OperationCanceledException) { StatusMessage = "搜尋已停止"; }
        catch (Exception exception) { StatusMessage = $"搜尋失敗：{exception.Message}"; }
        finally { IsBusy = false; }
    }

    private SearchRequest BuildRequest()
    {
        var range = ResolveDateRange();
        return new SearchRequest
        {
            Source = Source!, Keyword = Null(Keyword), From = range.From, To = range.To, TimeFrom = ParseTime(TimeFrom), TimeTo = ParseTime(TimeTo), UrlContains = Null(UrlFilter), ClientIp = Null(IpFilter), UserAgentContains = Null(UserAgentFilter), Username = Null(UsernameFilter), Method = Null(MethodFilter), StatusCode = int.TryParse(StatusFilter, out var status) ? status : null, MinTimeTakenMs = int.TryParse(MinTime, out var min) ? min : null, MaxTimeTakenMs = int.TryParse(MaxTime, out var max) ? max : null, PageSize = PageSize, MaxResults = MaxSearchResults
        };
    }

    private (DateTimeOffset? From, DateTimeOffset? To) ResolveDateRange()
    {
        var now = DateTimeOffset.Now;
        return QuickRange switch
        {
            "最近 15 分鐘" => (now.AddMinutes(-15).ToUniversalTime(), now.ToUniversalTime()),
            "最近 1 小時" => (now.AddHours(-1).ToUniversalTime(), now.ToUniversalTime()),
            "最近 24 小時" => (now.AddHours(-24).ToUniversalTime(), now.ToUniversalTime()),
            "最近 7 天" => (now.AddDays(-7).ToUniversalTime(), now.ToUniversalTime()),
            "今天" => (LocalDate(DateTime.Today), LocalDate(DateTime.Today.AddDays(1)).AddTicks(-1)),
            "昨天" => (LocalDate(DateTime.Today.AddDays(-1)), LocalDate(DateTime.Today).AddTicks(-1)),
            _ => (CustomDate(DateFrom, false), CustomDate(DateTo, true))
        };
    }

    private static DateTimeOffset? CustomDate(DateTime? date, bool endOfDay)
    {
        if (date is null) return null;
        var value = date.Value.Date.Add(endOfDay ? TimeSpan.FromDays(1).Subtract(TimeSpan.FromTicks(1)) : TimeSpan.Zero);
        return new DateTimeOffset(value, TimeZoneInfo.Local.GetUtcOffset(value)).ToUniversalTime();
    }

    private static DateTimeOffset LocalDate(DateTime date) => new DateTimeOffset(date.Date, TimeZoneInfo.Local.GetUtcOffset(date.Date)).ToUniversalTime();
    private static TimeSpan? ParseTime(string value) => TimeSpan.TryParse(value, out var result) ? result : null;

    private async IAsyncEnumerable<LogEntry> GetEntriesForAnalysis(SearchRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in _search.SearchAsync(request with { MaxResults = int.MaxValue }, cancellationToken).ConfigureAwait(false))
        {
            yield return result.Entry;
        }
    }
    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private void StopSearch() => _searchCancellation?.Cancel();
    private void StopIndex() => _indexCancellation?.Cancel();

    private async Task StartIndexAsync(SearchRequest request)
    {
        if (Interlocked.Exchange(ref _indexRunning, 1) == 1) return;
        _indexCancellation?.Cancel();
        _indexCancellation?.Dispose();
        _indexCancellation = new CancellationTokenSource();
        try
        {
            await _index.IndexAsync(request.Source, request, _indexCancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RunOnUi(() => IndexMessage = "Index 已停止，已保留完成的批次");
        }
        catch (Exception exception)
        {
            RunOnUi(() => IndexMessage = $"Index 失敗：{exception.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _indexRunning, 0);
            RunOnUi(RaiseCommands);
        }
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
        await RunAnalysisAsync(async token => IpResult = await _ipAnalyzer.AnalyzeAsync(GetEntriesForAnalysis(BuildRequest() with { ClientIp = ip }), ip, token), "IP 分析完成");
        ActivePage = "IP Analysis";
    }

    private async Task AnalyzeErrorsAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => ErrorResult = await _errorAnalyzer.AnalyzeAsync(GetEntriesForAnalysis(BuildRequest()), token), "錯誤分析完成");
        ActivePage = "Errors";
    }

    private async Task AnalyzeSlowAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => SlowResult = await _slowAnalyzer.AnalyzeAsync(GetEntriesForAnalysis(BuildRequest()), SlowThreshold, token), "慢請求分析完成");
        ActivePage = "Slow Requests";
    }

    private async Task AnalyzeTrafficAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => TrafficResult = await _trafficAnalyzer.AnalyzeAsync(GetEntriesForAnalysis(BuildRequest()), token), "流量分析完成");
        ActivePage = "Traffic";
    }

    private async Task AnalyzeSecurityAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => SecurityResult = await _securityAnalyzer.AnalyzeAsync(GetEntriesForAnalysis(BuildRequest()), token), "資安分析完成");
        ActivePage = "Security";
    }

    private async Task AnalyzeSecurityEntireSourceAsync()
    {
        if (Source is null) return;
        await RunAnalysisAsync(async token => SecurityResult = await _securityAnalyzer.AnalyzeAsync(GetEntriesForAnalysis(new SearchRequest { Source = Source!, MaxResults = int.MaxValue }), token), "資安分析完成（整個來源）");
        ActivePage = "Security";
    }

    private void StopAnalysis() => _analysisCancellation?.Cancel();

    private void UseRecentSource()
    {
        if (SelectedRecentSource is null) return;
        _ = SetSourceAsync(SelectedRecentSource);
    }

    private async Task RunAnalysisAsync(Func<CancellationToken, Task> action, string completedMessage)
    {
        _analysisCancellation?.Cancel(); _analysisCancellation = new CancellationTokenSource(); IsBusy = true; StatusMessage = "分析中...";
        try { await action(_analysisCancellation.Token); StatusMessage = completedMessage; } catch (OperationCanceledException) { StatusMessage = "分析已停止"; } catch (Exception exception) { StatusMessage = $"分析失敗：{exception.Message}"; } finally { IsBusy = false; }
    }

    private async Task StartRealtimeAsync()
    {
        if (Source is null) return;
        _realtimeSeen.Clear();
        await _realtime.StartAsync(Source); IsRealtime = true; StatusMessage = "Realtime Monitor ON";
    }

    private async Task StopRealtimeAsync()
    {
        await _realtime.StopAsync(); IsRealtime = false; StatusMessage = "Realtime Monitor OFF";
    }

    private void OnRealtimeEntriesAdded(object? sender, IReadOnlyList<LogEntry> entries)
    {
        RunOnUi(() =>
        {
            var request = BuildRequest();
            var added = 0;
            foreach (var entry in entries)
            {
                if (!MatchesFilter(entry, request)) continue;
                var key = $"{entry.LineNumber}|{entry.RawLine}";
                if (!_realtimeSeen.Add(key)) continue;
                Results.Insert(0, new SearchResult { Entry = entry, SourceFile = string.Empty, SourcePath = Source?.Path, IsIndexed = false });
                added++;
                while (Results.Count > MaxSearchResults) Results.RemoveAt(Results.Count - 1);
            }

            if (added > 0) StatusMessage = $"Realtime 新增 {added:N0} 筆";
        });
    }

    private static bool MatchesFilter(LogEntry entry, SearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Keyword) && !Contains(entry.RawLine, request.Keyword) && !Contains(entry.ClientIp, request.Keyword) && !Contains(entry.ResolvedClientIp, request.Keyword) && !Contains(entry.UriStem, request.Keyword) && !Contains(entry.UriQuery, request.Keyword) && !Contains(entry.UserAgent, request.Keyword) && !Contains(entry.Method, request.Keyword) && entry.StatusCode != (int.TryParse(request.Keyword, out var status) ? status : -1)) return false;
        if (request.From is not null && (entry.TimestampUtc is null || entry.TimestampUtc < request.From)) return false;
        if (request.To is not null && (entry.TimestampUtc is null || entry.TimestampUtc > request.To)) return false;
        if (request.TimeFrom is not null && (entry.TimestampUtc is null || entry.TimestampUtc.Value.TimeOfDay < request.TimeFrom)) return false;
        if (request.TimeTo is not null && (entry.TimestampUtc is null || entry.TimestampUtc.Value.TimeOfDay > request.TimeTo)) return false;
        if (!string.IsNullOrWhiteSpace(request.Method) && !string.Equals(entry.Method, request.Method, StringComparison.OrdinalIgnoreCase)) return false;
        if (request.StatusCode is not null && entry.StatusCode != request.StatusCode) return false;
        if (!string.IsNullOrWhiteSpace(request.ClientIp) && !Contains(entry.ClientIp, request.ClientIp) && !Contains(entry.ResolvedClientIp, request.ClientIp)) return false;
        if (!string.IsNullOrWhiteSpace(request.UrlContains) && !Contains(entry.DisplayUrl, request.UrlContains)) return false;
        if (!string.IsNullOrWhiteSpace(request.UserAgentContains) && !Contains(entry.UserAgent, request.UserAgentContains)) return false;
        if (request.MinTimeTakenMs is not null && (entry.TimeTakenMs is null || entry.TimeTakenMs < request.MinTimeTakenMs)) return false;
        if (request.MaxTimeTakenMs is not null && (entry.TimeTakenMs is null || entry.TimeTakenMs > request.MaxTimeTakenMs)) return false;
        if (!string.IsNullOrWhiteSpace(request.Username) && !Contains(entry.Username, request.Username)) return false;
        return true;

        static bool Contains(string? value, string? search) => value?.Contains(search ?? string.Empty, StringComparison.OrdinalIgnoreCase) == true;
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

    private async Task RefreshDbStatusAsync()
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
        try { if (json) await _export.ExportJsonAsync(_search.SearchAsync(BuildRequest()), dialog.FileName); else await _export.ExportCsvAsync(_search.SearchAsync(BuildRequest()), dialog.FileName); StatusMessage = "匯出完成"; } catch (Exception exception) { StatusMessage = $"匯出失敗：{exception.Message}"; } finally { IsBusy = false; }
    }

    private async Task ClearIndexAsync()
    {
        if (System.Windows.MessageBox.Show("確定清除 SQLite 索引？原始 IIS Log 不會被修改。", "確認", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes) return;
        await _index.ClearAsync(); IndexMessage = "索引已清除";
    }

    private async Task RebuildIndexAsync()
    {
        if (Source is null) return;
        IsBusy = true; try { await _index.RebuildAsync(Source); IndexMessage = "索引重建完成"; } catch (Exception exception) { IndexMessage = $"重建失敗：{exception.Message}"; } finally { IsBusy = false; }
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
