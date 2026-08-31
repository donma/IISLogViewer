using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using IISLogExplorer.App.ViewModels;
using IISLogExplorer.Core.Models;

namespace IISLogExplorer.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void ResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedResult is null) return;
        ShowDetail(viewModel.SelectedResult);
    }

    private void OnExit(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        var version = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(System.Reflection.Assembly.GetExecutingAssembly())?.InformationalVersion ?? "v.1.";
        System.Windows.MessageBox.Show($"IIS Log Explorer  {version}\n唯讀 IIS W3C Log 查詢與分析工具", "關於 IIS Log Explorer", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowDetail(SearchResult result)
    {
        var entry = result.Entry;
        var window = new Window
        {
            Title = "Request Detail",
            Width = 900,
            Height = 650,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = System.Windows.Application.Current.TryFindResource("WindowBrush") as System.Windows.Media.Brush
        };
        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        void CopyButton(string content, string text) { var button = new System.Windows.Controls.Button { Content = content, Margin = new Thickness(4) }; button.Click += (_, _) => System.Windows.Clipboard.SetText(text); buttons.Children.Add(button); }
        CopyButton("Copy URL", entry.DisplayUrl);
        CopyButton("Copy IP", entry.ResolvedClientIp ?? entry.ClientIp ?? string.Empty);
        CopyButton("Copy Raw Line", entry.RawLine ?? string.Empty);
        CopyButton("Copy JSON", System.Text.Json.JsonSerializer.Serialize(entry));
        Grid.SetRow(buttons, 0);
        grid.Children.Add(buttons);
        var detail = new System.Windows.Controls.TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = System.Windows.TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Text = $"""
            Timestamp:   {entry.TimestampLocal:yyyy-MM-dd HH:mm:ss}
            Method:      {entry.Method}
            URL:         {entry.DisplayUrl}
            Query:       {entry.UriQuery}
            Status:      {entry.DisplayStatus} (sc-status={entry.StatusCode}, sc-substatus={entry.SubStatusCode}, sc-win32-status={entry.Win32Status})
            Time Taken:  {entry.TimeTakenMs} ms
            Client IP:   {entry.ClientIp}
            Resolved IP: {entry.ResolvedClientIp}
            Server IP:   {entry.ServerIp}
            Username:    {entry.Username}
            User Agent:  {entry.UserAgent}
            Referer:     {entry.Referer}
             Source File: {result.SourcePath ?? result.SourceFile} line {entry.LineNumber}
            ------------------------------------------------------------
            Raw Log:
            {entry.RawLine}
            """ };
        Grid.SetRow(detail, 1);
        grid.Children.Add(detail);
        window.Content = grid;
        window.ShowDialog();
    }
}
