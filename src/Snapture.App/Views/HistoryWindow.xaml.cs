using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class HistoryWindow : Window
{
    private const int DominantColorTolerance = 90;
    private readonly CaptureHistoryService _history;
    private string? _dominantColorFilter;
    private bool _nearDuplicatesOnly;
    private bool _verifiedRedactedOnly;

    public sealed record ProjectChoice(long? Id, string Name)
    {
        public override string ToString() => Name;
    }

    public sealed class Row
    {
        public long Id { get; init; }
        public string FilePath { get; init; } = "";
        public BitmapSource? Thumbnail { get; init; }
        public string TitleLine { get; init; } = "";
        public string SubLine { get; init; } = "";
        public string ProjectLine { get; init; } = "";
        public bool IsVerifiedRedacted { get; init; }
        public string VerificationLine => IsVerifiedRedacted ? "Verified-redacted" : "";
        public string TimeLine { get; init; } = "";
        public string FeatureLine { get; init; } = "";
        public Brush? DominantColorBrush { get; init; }
        public string? OcrText { get; init; }
    }

    public HistoryWindow(CaptureHistoryService history)
    {
        InitializeComponent();
        _history = history;
        DbPathText.Text = $"Index: {CaptureHistoryService.DbPath}";
        PopulateFilters();
        PopulateProjects();
        LoadRecent();
    }

    private void PopulateFilters()
    {
        AppFilter.Items.Clear();
        DateFilter.Items.Clear();
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

    private void PopulateProjects(long? selectedProjectId = null)
    {
        ProjectFilter.Items.Clear();
        ProjectFilter.Items.Add(new ProjectChoice(null, "All projects"));
        foreach (var project in _history.Projects())
            ProjectFilter.Items.Add(new ProjectChoice(project.Id, project.Name));

        int selectedIndex = 0;
        if (selectedProjectId is not null)
        {
            for (int i = 0; i < ProjectFilter.Items.Count; i++)
            {
                if (ProjectFilter.Items[i] is ProjectChoice choice && choice.Id == selectedProjectId)
                {
                    selectedIndex = i;
                    break;
                }
            }
        }

        ProjectFilter.SelectedIndex = selectedIndex;
    }

    private void OnProjectFilterChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => ApplyFilters();

    private void OnNewProjectClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new HistoryProjectDialog { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var projectId = _history.CreateProject(dialog.ProjectName);
            PopulateProjects(projectId);
            ApplyFilters();
            StatusText.Text = $"Project created: {dialog.ProjectName}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not create project: {ex.Message}";
        }
    }

    private void OnMoveSelectedClicked(object sender, RoutedEventArgs e)
    {
        var selected = HistoryList.SelectedItems.Cast<Row>().ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "Select one or more captures first.";
            return;
        }

        if (ProjectFilter.SelectedItem is not ProjectChoice { Id: { } projectId })
        {
            StatusText.Text = "Choose a project before moving captures.";
            return;
        }

        try
        {
            _history.AssignToProject(selected.Select(row => row.Id), projectId);
            ApplyFilters();
            StatusText.Text = $"Moved {selected.Length} capture{(selected.Length == 1 ? "" : "s")}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not move captures: {ex.Message}";
        }
    }

    private void OnUnassignSelectedClicked(object sender, RoutedEventArgs e)
    {
        var selected = HistoryList.SelectedItems.Cast<Row>().ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "Select one or more captures first.";
            return;
        }

        try
        {
            _history.AssignToProject(selected.Select(row => row.Id), projectId: null);
            ApplyFilters();
            StatusText.Text = $"Returned {selected.Length} capture{(selected.Length == 1 ? "" : "s")} to Inbox.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not unassign captures: {ex.Message}";
        }
    }

    private async void OnBackupLibraryClicked(object sender, RoutedEventArgs e)
    {
        var path = await StoragePickerService.PickSaveFileAsync(
            this,
            "Snapture library (*.snapture-library)|*.snapture-library",
            $"snapture-library-{DateTime.Now:yyyyMMdd-HHmm}.snapture-library",
            ".snapture-library",
            new[]
            {
                new StoragePickerService.FileTypeChoice(
                    "Snapture library",
                    new[] { ".snapture-library" })
            },
            title: "Choose a Snapture history backup");
        if (path is null)
            return;

        try
        {
            var backupPath = _history.ExportLibrary(path);
            StatusText.Text = $"Library backup created: {Path.GetFileName(backupPath)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Backup failed: {ex.Message}";
        }
    }

    private async void OnRestoreLibraryClicked(object sender, RoutedEventArgs e)
    {
        var path = await StoragePickerService.PickOpenFileAsync(
            this,
            "Snapture library (*.snapture-library)|*.snapture-library|All files (*.*)|*.*",
            new[] { ".snapture-library" },
            title: "Choose a Snapture history backup");
        if (path is null)
            return;

        try
        {
            var result = _history.ImportLibrary(path);
            PopulateFilters();
            PopulateProjects();
            ApplyFilters();
            StatusText.Text = $"Restored {result.ImportedCaptures} capture{(result.ImportedCaptures == 1 ? "" : "s")}" +
                              (result.SkippedCaptures == 0 ? "." : $"; skipped {result.SkippedCaptures}.");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Restore failed: {ex.Message}";
        }
    }

    private void ApplyFilters()
    {
        var all = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? _history.Recent(_dominantColorFilter is null ? 500 : 5000)
            : _history.Search(SearchBox.Text, 5000);
        if (_dominantColorFilter is not null)
        {
            all = all
                .Where(entry => ImageFeatureService.ColorDistance(entry.DominantColorHex, _dominantColorFilter)
                    <= DominantColorTolerance)
                .ToList();
        }

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
            (dateCutoff is null || e.CapturedAtUtc >= dateCutoff) &&
            (!_verifiedRedactedOnly || e.VerifiedRedacted) &&
            (ProjectFilter.SelectedItem is not ProjectChoice project
                || project.Id is null
                || e.ProjectId == project.Id)).ToList();
        if (_nearDuplicatesOnly)
        {
            var duplicateIds = _history.FindNearDuplicateIds(filtered);
            filtered = filtered.Where(entry => duplicateIds.Contains(entry.Id)).ToList();
        }
        Populate(filtered);
    }

    private void LoadRecent() => ApplyFilters();

    private void Populate(IReadOnlyList<HistoryEntry> entries)
    {
        var duplicateIds = _history.FindNearDuplicateIds(entries);
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
                ProjectLine = e.ProjectName is null ? "Inbox · unassigned" : $"Project · {e.ProjectName}",
                IsVerifiedRedacted = e.VerifiedRedacted,
                TimeLine = e.CapturedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                FeatureLine = BuildFeatureLine(e, duplicateIds.Contains(e.Id)),
                DominantColorBrush = CreateColorBrush(e.DominantColorHex),
                OcrText = e.OcrText
            });
        }
        HistoryList.ItemsSource = rows;
        EmptyState.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = rows.Count == 0 ? "No captures" : $"{rows.Count} captures";
    }

    private static string BuildFeatureLine(HistoryEntry entry, bool nearDuplicate)
    {
        var color = entry.DominantColorHex ?? "color —";
        var hash = string.IsNullOrWhiteSpace(entry.PerceptualHash)
            ? "pHash —"
            : $"pHash {entry.PerceptualHash[..Math.Min(8, entry.PerceptualHash.Length)]}…";
        return nearDuplicate ? $"{color} · {hash} · near duplicate" : $"{color} · {hash}";
    }

    private static Brush? CreateColorBrush(string? hex)
    {
        if (!ImageFeatureService.TryParseHex(hex, out var color))
            return null;

        var brush = new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue));
        brush.Freeze();
        return brush;
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
        if (e.Key == Key.Escape)
            SearchBox.Text = "";
        ApplyFilters();
    }

    private void OnColorSearchClicked(object sender, RoutedEventArgs e)
    {
        var query = ColorSearchBox.Text.Trim();
        if (query.Length == 0)
        {
            _dominantColorFilter = null;
            ApplyFilters();
            return;
        }

        if (!ImageFeatureService.TryParseHex(query, out var color))
        {
            StatusText.Text = "Use a 6-digit RGB color such as #CBA6F7.";
            return;
        }

        _dominantColorFilter = ImageFeatureService.ToHex(color);
        ApplyFilters();
    }

    private void OnNearDuplicatesClicked(object sender, RoutedEventArgs e)
    {
        _nearDuplicatesOnly = !_nearDuplicatesOnly;
        NearDuplicateButton.Content = _nearDuplicatesOnly ? "Show all" : "Near duplicates";
        ApplyFilters();
    }

    private void OnClearFeatureFiltersClicked(object sender, RoutedEventArgs e)
    {
        ColorSearchBox.Clear();
        _dominantColorFilter = null;
        _nearDuplicatesOnly = false;
        _verifiedRedactedOnly = false;
        NearDuplicateButton.Content = "Near duplicates";
        VerifiedRedactedButton.Content = "Verified-redacted only";
        ApplyFilters();
    }

    private void OnVerifiedRedactedClicked(object sender, RoutedEventArgs e)
    {
        _verifiedRedactedOnly = !_verifiedRedactedOnly;
        VerifiedRedactedButton.Content = _verifiedRedactedOnly ? "Show all captures" : "Verified-redacted only";
        ApplyFilters();
    }

    private Row? Selected => HistoryList.SelectedItem as Row;

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Selected is { } row && File.Exists(row.FilePath))
            EditorTabHostWindow.Open(new EditorWindow(row.FilePath));
    }

    private void OnOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (Selected is { } row && File.Exists(row.FilePath))
            EditorTabHostWindow.Open(new EditorWindow(row.FilePath));
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
            new OcrResultWindow(text, engine: result?.Engine).Show();
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
        ApplyFilters();
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
