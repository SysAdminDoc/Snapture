using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class HistoryWindow : Window
{
    private readonly CaptureHistoryService _history;

    public sealed class Row
    {
        public long Id { get; init; }
        public string FilePath { get; init; } = "";
        public BitmapSource? Thumbnail { get; init; }
        public string TitleLine { get; init; } = "";
        public string SubLine { get; init; } = "";
        public string TimeLine { get; init; } = "";
        public string? OcrText { get; init; }
    }

    public HistoryWindow(CaptureHistoryService history)
    {
        InitializeComponent();
        _history = history;
        DbPathText.Text = $"Index: {CaptureHistoryService.DbPath}";
        PopulateFilters();
        LoadRecent();
    }

    private void PopulateFilters()
    {
        AppFilter.Items.Add("All apps");
        var entries = _history.Recent(500);
        var apps = entries.Select(e => e.SourceApp).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().Order();
        foreach (var app in apps) AppFilter.Items.Add(app!);
        AppFilter.SelectedIndex = 0;

        DateFilter.Items.Add("Any date");
        DateFilter.Items.Add("Today");
        DateFilter.Items.Add("Last 7 days");
        DateFilter.Items.Add("Last 30 days");
        DateFilter.SelectedIndex = 0;
    }

    private void OnFilterChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ApplyFilters();

    private void ApplyFilters()
    {
        var all = string.IsNullOrWhiteSpace(SearchBox.Text) ? _history.Recent(500) : _history.Search(SearchBox.Text, 500);
        string? appFilter = AppFilter.SelectedIndex > 0 ? AppFilter.SelectedItem as string : null;
        DateTime? dateCutoff = DateFilter.SelectedIndex switch
        {
            1 => DateTime.UtcNow.Date,
            2 => DateTime.UtcNow.AddDays(-7),
            3 => DateTime.UtcNow.AddDays(-30),
            _ => null
        };
        var filtered = all.Where(e =>
            (appFilter is null || e.SourceApp == appFilter) &&
            (dateCutoff is null || e.CapturedAtUtc >= dateCutoff)).ToList();
        Populate(filtered);
    }

    private void LoadRecent() => Populate(_history.Recent());

    private void Populate(IReadOnlyList<HistoryEntry> entries)
    {
        var rows = new List<Row>();
        foreach (var e in entries)
        {
            rows.Add(new Row
            {
                Id = e.Id,
                FilePath = e.FilePath,
                Thumbnail = LoadThumbnail(e.FilePath),
                TitleLine = string.IsNullOrWhiteSpace(e.WindowTitle) ? Path.GetFileName(e.FilePath) : e.WindowTitle!,
                SubLine = $"{e.Source} · {(e.SourceApp ?? "—")} · {e.Width}×{e.Height}",
                TimeLine = e.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                OcrText = e.OcrText
            });
        }
        HistoryList.ItemsSource = rows;
        StatusText.Text = $"{rows.Count} captures";
    }

    private static BitmapSource? LoadThumbnail(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.DecodePixelWidth = 240;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { SearchBox.Text = ""; LoadRecent(); return; }
        var q = SearchBox.Text;
        if (string.IsNullOrWhiteSpace(q)) LoadRecent();
        else Populate(_history.Search(q));
    }

    private Row? Selected => HistoryList.SelectedItem as Row;

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } row && File.Exists(row.FilePath))
            new EditorWindow(row.FilePath).Show();
    }

    private void OnOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row && File.Exists(row.FilePath))
            new EditorWindow(row.FilePath).Show();
    }

    private void OnPinSelected(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row && File.Exists(row.FilePath))
        {
            try
            {
                var bi = new BitmapImage(new Uri(row.FilePath));
                new PinWindow(bi).Show();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not pin: {ex.Message}";
            }
        }
    }

    private async void OnRunOcr(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || !File.Exists(row.FilePath)) return;
        try
        {
            var bi = new BitmapImage(new Uri(row.FilePath));
            var result = await OcrService.RecognizeAsync(bi);
            string text = result?.Text ?? "";
            _history.UpdateOcrText(row.Id, text);
            new OcrResultWindow(text).Show();
            StatusText.Text = "OCR indexed.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"OCR failed: {ex.Message}";
        }
    }

    private void OnSendToLanShare(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row || !File.Exists(row.FilePath)) return;
        if (App.Host is null) return;
        if (!App.Host.LanShare.IsRunning && !App.Host.TryStartLanShare())
        {
            StatusText.Text = "LAN share is off — open Settings → LAN share to configure it.";
            return;
        }
        try
        {
            var ttl = TimeSpan.FromMinutes(App.Host.Settings.Current.LanShareTtlMinutes);
            var url = App.Host.LanShare.Register(row.FilePath, ttl);
            try { System.Windows.Clipboard.SetText(url); } catch { }
            StatusText.Text = $"LAN URL copied: {url}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Share failed: {ex.Message}";
        }
    }

    private void OnRevealInFolder(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.FilePath}\"") { UseShellExecute = true }); }
        catch { }
    }

    private void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } row) return;
        _history.Delete(row.Id);
        if (string.IsNullOrWhiteSpace(SearchBox.Text)) LoadRecent();
        else Populate(_history.Search(SearchBox.Text));
    }

    private async void OnOcrAllClicked(object sender, RoutedEventArgs e)
    {
        OcrAllButton.IsEnabled = false;
        try
        {
            int done = 0;
            foreach (var entry in _history.Recent(500))
            {
                if (!string.IsNullOrEmpty(entry.OcrText)) continue;
                if (!File.Exists(entry.FilePath)) continue;
                try
                {
                    var bi = new BitmapImage(new Uri(entry.FilePath));
                    var r = await OcrService.RecognizeAsync(bi);
                    var text = r?.Text ?? "";
                    _history.UpdateOcrText(entry.Id, text);
                    done++;
                    StatusText.Text = $"OCR'd {done} entries…";
                }
                catch { /* skip and continue */ }
            }
            StatusText.Text = $"Done. OCR'd {done} new entries.";
            LoadRecent();
        }
        finally { OcrAllButton.IsEnabled = true; }
    }
}
