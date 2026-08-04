using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Imports and manages inert ShareX-compatible declarative uploader profiles.</summary>
public sealed class DeclarativeUploaderConfigurationWindow : Window
{
    private readonly List<DeclarativeUploaderProfile> _profiles;
    private readonly ListBox _profileList;
    private readonly TextBlock _details;
    private readonly TextBlock _status;

    public IReadOnlyList<DeclarativeUploaderProfile> Profiles =>
        _profiles.Select(profile => profile.Clone()).ToList();

    public DeclarativeUploaderConfigurationWindow(IEnumerable<DeclarativeUploaderProfile>? profiles)
    {
        _profiles = (profiles ?? Array.Empty<DeclarativeUploaderProfile>())
            .Select(profile => profile.Clone())
            .ToList();

        Title = "Snapture — Declarative uploaders";
        Width = 700;
        Height = 480;
        MinWidth = 540;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Declarative uploaders",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        root.Children.Add(heading);

        var body = new Grid { Margin = new Thickness(0, 12, 0, 12) };
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _profileList = new ListBox();
        AutomationProperties.SetName(_profileList, "Declarative uploader profiles");
        _profileList.SelectionChanged += OnSelectionChanged;
        Grid.SetRow(_profileList, 0);
        body.Children.Add(_profileList);
        _details = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        };
        _details.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        Grid.SetRow(_details, 1);
        body.Children.Add(_details);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new DockPanel { LastChildFill = false };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 380, VerticalAlignment = VerticalAlignment.Center };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        DockPanel.SetDock(_status, Dock.Left);
        footer.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var import = new Button { Content = "Import .sxcu / JSON" };
        import.SetResourceReference(StyleProperty, "AccentButton");
        AutomationProperties.SetName(import, "Import ShareX custom uploader JSON");
        import.Click += OnImportClicked;
        var remove = new Button { Content = "Remove", Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += OnRemoveClicked;
        var save = new Button { Content = "Save", Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        save.Click += (_, _) => { DialogResult = true; Close(); };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(8, 0, 0, 0), IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        actions.Children.Add(import);
        actions.Children.Add(remove);
        actions.Children.Add(save);
        actions.Children.Add(cancel);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        RefreshList();
        _status.Text = _profiles.Count == 0
            ? "Import a ShareX .sxcu or compatible JSON file. Imported profiles never upload automatically."
            : $"{_profiles.Count} profile(s) available. Select one from the editor or tray to run it.";
    }

    private void RefreshList()
    {
        _profileList.Items.Clear();
        foreach (var profile in _profiles)
            _profileList.Items.Add(new ListBoxItem { Content = profile, Tag = profile });
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_profileList.SelectedItem is not ListBoxItem { Tag: DeclarativeUploaderProfile profile })
        {
            _details.Text = string.Empty;
            return;
        }
        _details.Text = $"{profile.RequestMethod} {profile.RequestUrl}\nBody: {DeclarativeUploaderBodyTypes.Normalize(profile.Body)} · Destination: {profile.DestinationType}\nImported uploaders are user-controlled and run only after an explicit editor or tray action.";
    }

    private void OnImportClicked(object sender, RoutedEventArgs e)
    {
        _ = ImportAsync();
    }

    private async Task ImportAsync()
    {
        var path = await StoragePickerService.PickOpenFileAsync(
            this,
            "ShareX custom uploader (*.sxcu;*.json)|*.sxcu;*.json|All files (*.*)|*.*",
            new[] { ".sxcu", ".json" },
            title: "Import a declarative uploader");
        if (path is null) return;
        try
        {
            var profile = DeclarativeUploaderService.ImportJson(await File.ReadAllTextAsync(path), Path.GetFileName(path));
            if (_profiles.Any(existing => string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
                throw new DeclarativeUploaderException($"An uploader named '{profile.Name}' is already imported.");
            _profiles.Add(profile);
            RefreshList();
            _profileList.SelectedIndex = _profiles.Count - 1;
            _status.Text = $"Imported {profile.Name}. Click Save to keep it.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Import failed: {ex.Message}";
        }
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (_profileList.SelectedIndex < 0 || _profileList.SelectedIndex >= _profiles.Count)
        {
            _status.Text = "Select an uploader to remove.";
            return;
        }
        string name = _profiles[_profileList.SelectedIndex].Name;
        _profiles.RemoveAt(_profileList.SelectedIndex);
        RefreshList();
        _status.Text = $"Removed {name}. Click Save to keep the change.";
    }
}
