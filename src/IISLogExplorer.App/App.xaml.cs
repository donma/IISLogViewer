using System.Windows;
using IISLogExplorer.App.ViewModels;
using IISLogExplorer.Core.Analysis;
using IISLogExplorer.Core.Configuration;
using IISLogExplorer.Core.Exporting;
using IISLogExplorer.Core.Files;
using IISLogExplorer.Core.IIS;
using IISLogExplorer.Core.Indexing;
using IISLogExplorer.Core.Networking;
using IISLogExplorer.Core.Parsing;
using IISLogExplorer.Core.Realtime;
using IISLogExplorer.Core.Searching;
using IISLogExplorer.Core.Security;
using IISLogExplorer.Infrastructure.Analysis;
using IISLogExplorer.Infrastructure.Configuration;
using IISLogExplorer.Infrastructure.Database;
using IISLogExplorer.Infrastructure.Exporting;
using IISLogExplorer.Infrastructure.Files;
using IISLogExplorer.Infrastructure.IIS;
using IISLogExplorer.Infrastructure.Indexing;
using IISLogExplorer.Infrastructure.Logging;
using IISLogExplorer.Infrastructure.Searching;
using IISLogExplorer.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IISLogExplorer.App;

public partial class App : System.Windows.Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalHandlers();
        try
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((_, configuration) => configuration.SetBasePath(AppContext.BaseDirectory).AddJsonFile("settings.json", optional: true, reloadOnChange: false))
                .ConfigureServices((_, services) => ConfigureServices(services))
                .Build();
            await _host.StartAsync();
            Services = _host.Services;
            await Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            var window = Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            if (Services is not null) await Services.GetRequiredService<AppLogger>().LogAsync("Application startup failed", exception);
            System.Windows.MessageBox.Show($"無法啟動應用程式：{exception.Message}", "IIS Log Explorer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try { await _host.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(true); } catch { }
            _host.Dispose();
        }
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<SourceRepository>();
        services.AddSingleton<LogFileRepository>();
        services.AddSingleton<LogEntryRepository>();
        services.AddSingleton<FieldsHeaderParser>();
        services.AddSingleton<ClientIpResolver>();
        services.AddSingleton<IIisLogParser, IisW3cLogParser>();
        services.AddSingleton<IisW3cLogParser>();
        services.AddSingleton<ILogFileScanner, LogFileScanner>();
        services.AddSingleton<LogFileScanner>();
        services.AddSingleton<FileFingerprintService>();
        services.AddSingleton<IIndexService, SqliteIndexService>();
        services.AddSingleton<ISearchService, HybridSearchService>();
        services.AddSingleton<IIisDiscoveryService, IisDiscoveryService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<SecurityRuleEngine>(_ => new SecurityRuleEngine());
        services.AddSingleton<ISecurityAnalyzer, SecurityAnalyzer>();
        services.AddSingleton<IIpAnalyzer, IpAnalyzer>();
        services.AddSingleton<IErrorAnalyzer, ErrorAnalyzer>();
        services.AddSingleton<ISlowRequestAnalyzer, SlowRequestAnalyzer>();
        services.AddSingleton<ITrafficAnalyzer, TrafficAnalyzer>();
        services.AddSingleton<IRealtimeMonitor, RealtimeLogWatcher>();
        services.AddSingleton<AppLogger>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void RegisterGlobalHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            _ = LogUnhandledAsync(args.Exception);
            System.Windows.MessageBox.Show($"發生未預期錯誤：{args.Exception.Message}", "IIS Log Explorer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            _ = LogUnhandledAsync(args.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception) _ = LogUnhandledAsync(exception);
        };
    }

    private static Task LogUnhandledAsync(Exception exception) => Services is null ? Task.CompletedTask : Services.GetRequiredService<AppLogger>().LogAsync("Unhandled exception", exception);
}
