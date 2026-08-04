using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Small editor for user-owned external command profiles.</summary>
public sealed class ExternalCommandConfigurationWindow : Window
{
    private readonly List<ExternalCommandProfile> _profiles;
    private readonly ListBox _profileList;
    private readonly TextBox _nameBox;
    private readonly TextBox _executableBox;
    private readonly TextBox _argumentsBox;
    private readonly ComboBox _inputModeCombo;
    private readonly TextBox _timeoutBox;
    private readonly TextBlock _statusText;
    private int _selectedIndex = -1;

    public IReadOnlyList<ExternalCommandProfile> Profiles =>
        _profiles.Select(profile => profile.Clone()).ToList();

    public ExternalCommandConfigurationWindow(IEnumerable<ExternalCommandProfile>? profiles)
    {
        _profiles = (profiles ?? Array.Empty<ExternalCommandProfile>())
            .Select(profile => profile.Clone())
            .ToList();

        Title = "Snapture — External commands";
        Width = 820;
        Height = 560;
        MinWidth = 680;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid { Margin = new Thickness(18) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "External command destinations",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        Grid.SetColumnSpan(heading, 2);
        root.Children.Add(heading);

        _profileList = new ListBox
        {
            Margin = new Thickness(0, 12, 14, 0),
            Background = null,
            BorderThickness = new Thickness(0)
        };
        AutomationProperties.SetName(_profileList, "External command profiles");
        _profileList.SelectionChanged += OnProfileSelectionChanged;
        Grid.SetRow(_profileList, 1);
        root.Children.Add(_profileList);

        var editor = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        editor.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _nameBox = AddField(editor, 0, "Name");
        _executableBox = AddField(editor, 1, "Executable or PATH command");
        var executableRow = (Grid)_executableBox.Parent;
        var browse = new Button { Content = "Browse…", Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(browse, "Browse for external command executable");
        browse.Click += OnBrowseClicked;
        Grid.SetColumn(browse, 2);
        executableRow.Children.Add(browse);

        _argumentsBox = AddField(editor, 2, "Arguments");
        _argumentsBox.ToolTip = "Use {file}, {source}, {width}, {height}, or {timestamp}. Placeholders are passed as one safe argument; do not add quotes around them.";
        _inputModeCombo = AddComboField(editor, 3, "Input", (ExternalCommandInputModes.FileArgument, "Temporary PNG path"), (ExternalCommandInputModes.Stdin, "PNG on stdin"));
        _timeoutBox = AddField(editor, 4, "Timeout (seconds)");
        _timeoutBox.ToolTip = $"1–{ExternalCommandService.MaxTimeoutSeconds} seconds.";

        var help = new TextBlock
        {
            Text = "Commands run directly with shell execution disabled. File mode requires {file}; stdin mode writes the PNG bytes to standard input. Output is captured for the completion toast and truncated at 128 KB.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 14, 0, 0)
        };
        help.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        var addButton = new Button { Content = "Add / replace", HorizontalAlignment = HorizontalAlignment.Left };
        addButton.SetResourceReference(StyleProperty, "AccentButton");
        addButton.Click += OnAddOrReplaceClicked;
        AutomationProperties.SetName(addButton, "Add or replace external command profile");
        Grid.SetRow(addButton, 5);
        editor.Children.Add(addButton);

        Grid.SetRow(help, 6);
        editor.Children.Add(help);
        Grid.SetColumn(editor, 1);
        Grid.SetRow(editor, 1);
        root.Children.Add(editor);

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        _statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 470, VerticalAlignment = VerticalAlignment.Center };
        _statusText.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        DockPanel.SetDock(_statusText, Dock.Left);
        footer.Children.Add(_statusText);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var newButton = new Button { Content = "New", Margin = new Thickness(0, 0, 8, 0) };
        newButton.Click += (_, _) => ClearEditor();
        var removeButton = new Button { Content = "Remove", Margin = new Thickness(0, 0, 8, 0) };
        removeButton.Click += OnRemoveClicked;
        var saveButton = new Button { Content = "Save", IsDefault = true };
        saveButton.SetResourceReference(StyleProperty, "AccentButton");
        saveButton.Click += (_, _) => { DialogResult = true; Close(); };
        var cancelButton = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        cancelButton.Click += (_, _) => { DialogResult = false; Close(); };
        actions.Children.Add(newButton);
        actions.Children.Add(removeButton);
        actions.Children.Add(saveButton);
        actions.Children.Add(cancelButton);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        Grid.SetColumnSpan(footer, 2);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        RefreshList();
        ClearEditor();
    }

    private TextBox AddField(Grid grid, int row, string label)
    {
        var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(caption, 0);
        rowGrid.Children.Add(caption);
        var box = new TextBox { MinHeight = 28 };
        AutomationProperties.SetName(box, label);
        Grid.SetColumn(box, 1);
        rowGrid.Children.Add(box);
        Grid.SetRow(rowGrid, row);
        grid.Children.Add(rowGrid);
        return box;
    }

    private ComboBox AddComboField(Grid grid, int row, string label, params (string Tag, string Content)[] choices)
    {
        var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rowGrid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var combo = new ComboBox { MinHeight = 28 };
        AutomationProperties.SetName(combo, label);
        foreach (var choice in choices)
            combo.Items.Add(new ComboBoxItem { Tag = choice.Tag, Content = choice.Content });
        combo.SelectedIndex = 0;
        Grid.SetColumn(combo, 1);
        rowGrid.Children.Add(combo);
        Grid.SetRow(rowGrid, row);
        grid.Children.Add(rowGrid);
        return combo;
    }

    private void RefreshList()
    {
        _profileList.Items.Clear();
        foreach (var profile in _profiles)
            _profileList.Items.Add(new ListBoxItem { Content = profile, Tag = profile });
    }

    private void OnProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedIndex = _profileList.SelectedIndex;
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count) return;
        var profile = _profiles[_selectedIndex];
        _nameBox.Text = profile.Name;
        _executableBox.Text = profile.ExecutablePath;
        _argumentsBox.Text = profile.Arguments;
        SelectComboByTag(_inputModeCombo, ExternalCommandInputModes.Normalize(profile.InputMode));
        _timeoutBox.Text = profile.TimeoutSeconds.ToString();
        _statusText.Text = "Edit the selected profile, then click New to create a separate entry.";
    }

    private void ClearEditor()
    {
        _selectedIndex = -1;
        _profileList.SelectedIndex = -1;
        _nameBox.Text = "External command";
        _executableBox.Text = string.Empty;
        _argumentsBox.Text = "{file}";
        SelectComboByTag(_inputModeCombo, ExternalCommandInputModes.FileArgument);
        _timeoutBox.Text = "30";
        _statusText.Text = "Add a command profile. The executable may be a full path or a command available on PATH.";
    }

    private void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        _ = BrowseAsync();
    }

    private async Task BrowseAsync()
    {
        var path = await StoragePickerService.PickOpenFileAsync(
            this,
            "Executable (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*",
            new[] { ".exe", ".cmd", ".bat" },
            title: "Choose an external command");
        if (path is not null)
            _executableBox.Text = path;
    }

    private void OnRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _profiles.Count)
        {
            _statusText.Text = "Select a command profile to remove.";
            return;
        }
        _profiles.RemoveAt(_selectedIndex);
        RefreshList();
        ClearEditor();
        _statusText.Text = "Profile removed. Click Save to keep the change.";
    }

    private void OnAddOrReplaceClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var profile = ReadProfile();
            ExternalCommandService.ValidateProfile(profile);
            if (_selectedIndex >= 0 && _selectedIndex < _profiles.Count)
                _profiles[_selectedIndex] = profile;
            else
            {
                _profiles.Add(profile);
                _selectedIndex = _profiles.Count - 1;
            }
            RefreshList();
            _profileList.SelectedIndex = _selectedIndex;
            _statusText.Text = "Profile staged. Click Save to persist it.";
        }
        catch (Exception ex)
        {
            _statusText.Text = ex.Message;
        }
    }

    private ExternalCommandProfile ReadProfile()
    {
        if (!int.TryParse(_timeoutBox.Text, out int timeout))
            timeout = 30;
        return new ExternalCommandProfile
        {
            Name = _nameBox.Text.Trim(),
            ExecutablePath = _executableBox.Text.Trim(),
            Arguments = _argumentsBox.Text.Trim(),
            InputMode = (_inputModeCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? ExternalCommandInputModes.FileArgument,
            TimeoutSeconds = timeout
        };
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }
}
