using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using IISLogExplorer.Core.Models;
using IISLogExplorer.Core.Searching;
using IISLogExplorer.Infrastructure.Logging;

namespace IISLogExplorer.App.ViewModels;

public sealed class SearchViewModel : INotifyPropertyChanged
{
    private readonly ISearchService _search;
    private readonly AppLogger _logger;
    private CancellationTokenSource? _searchCancellation;
    private LogSource? _source;
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
    private int _pageSize;
    private int _maxSearchResults;
    private bool _isBusy;
    private string _statusMessage = "尚未選擇 IIS Log";
    private SearchResult? _selectedResult;

    public SearchViewModel(ISearchService search, AppLogger logger)
    {
        _search = search;
        _logger = logger;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LogSource? Source
    {
        get => _source;
        set
        {
            _source = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<SearchResult> Results { get; } = new();

    public int PageSize { get => _pageSize; set { _pageSize = value; OnPropertyChanged(); } }
    public int MaxSearchResults { get => _maxSearchResults; set { _maxSearchResults = Math.Clamp(value, 100, 100000); OnPropertyChanged(); } }
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
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; OnPropertyChanged(); } }
    public string StatusMessage { get => _statusMessage; private set { _statusMessage = value; OnPropertyChanged(); } }
    public SearchResult? SelectedResult { get => _selectedResult; set { _selectedResult = value; OnPropertyChanged(); } }

    public async Task SearchAsync(Action<int>? onStarted = null, Action? onBusyChanged = null, Action<string>? onStatusChanged = null)
    {
        if (Source is null)
        {
            SetStatus("尚未選擇 IIS Log 來源", onStatusChanged);
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        Results.Clear();
        IsBusy = true;
        onBusyChanged?.Invoke();
        SetStatus("Searching...", onStatusChanged);
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var count = 0;
        try
        {
            var request = BuildRequest();
            onStarted?.Invoke(request.MaxResults);
            await foreach (var result in _search.SearchAsync(request, _searchCancellation.Token).ConfigureAwait(true))
            {
                if (Results.Count < request.MaxResults) Results.Add(result);
                count++;
                if (count % 100 == 0) SetStatus($"搜尋 {count:N0} 筆", onStatusChanged);
            }

            stopwatch.Stop();
            await _logger.LogAsync($"Search done; keyword={request.Keyword ?? "-"} count={count} elapsed={stopwatch.Elapsed.TotalSeconds:0.###}s").ConfigureAwait(true);
            SetStatus(count == 0 ? "找不到符合條件的 Request" : $"搜尋 {count:N0} 筆", onStatusChanged);
        }
        catch (OperationCanceledException)
        {
            SetStatus("搜尋已停止", onStatusChanged);
        }
        catch (Exception exception)
        {
            SetStatus($"搜尋失敗：{exception.Message}", onStatusChanged);
            await _logger.LogAsync("Search failed", exception).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
            onBusyChanged?.Invoke();
        }
    }

    public void StopSearch() => _searchCancellation?.Cancel();

    public void InsertRealtime(LogEntry entry)
    {
        Results.Insert(0, new SearchResult { Entry = entry, SourceFile = string.Empty, SourcePath = Source?.Path, IsIndexed = false });
        while (Results.Count > MaxSearchResults) Results.RemoveAt(Results.Count - 1);
    }

    public SearchRequest BuildRequest()
    {
        var range = ResolveDateRange();
        return new SearchRequest
        {
            Source = Source!, Keyword = Null(Keyword), From = range.From, To = range.To, TimeFrom = ParseTime(TimeFrom), TimeTo = ParseTime(TimeTo), UrlContains = Null(UrlFilter), ClientIp = Null(IpFilter), UserAgentContains = Null(UserAgentFilter), Username = Null(UsernameFilter), Method = Null(MethodFilter), StatusCode = int.TryParse(StatusFilter, out var status) ? status : null, MinTimeTakenMs = int.TryParse(MinTime, out var min) ? min : null, MaxTimeTakenMs = int.TryParse(MaxTime, out var max) ? max : null, PageSize = PageSize, MaxResults = MaxSearchResults
        };
    }

    public async IAsyncEnumerable<SearchResult> SearchResults(SearchRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in _search.SearchAsync(request, cancellationToken).ConfigureAwait(false))
        {
            yield return result;
        }
    }

    public async IAsyncEnumerable<LogEntry> GetEntriesForAnalysis(SearchRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var result in _search.SearchAsync(request with { MaxResults = int.MaxValue }, cancellationToken).ConfigureAwait(false))
        {
            yield return result.Entry;
        }
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
    private static string? Null(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void SetStatus(string message, Action<string>? onStatusChanged)
    {
        StatusMessage = message;
        onStatusChanged?.Invoke(message);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}