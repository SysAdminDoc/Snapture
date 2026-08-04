using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Opt-in configuration for the built-in Nextcloud and Immich destinations.</summary>
public sealed class SelfHostedDestinationsWindow : Window
{
    private readonly NextcloudDestinationSettings _nextcloud;
    private readonly ImmichDestinationSettings _immich;
    private readonly CheckBox _nextcloudEnabled;
    private TextBox _nextcloudServer = null!;
    private TextBox _nextcloudUser = null!;
    private TextBox _nextcloudFolder = null!;
    private PasswordBox _nextcloudCredential = null!;
    private CheckBox _nextcloudClearCredential = null!;
    private readonly CheckBox _immichEnabled;
    private TextBox _immichServer = null!;
    private TextBox _immichAlbum = null!;
    private PasswordBox _immichCredential = null!;
    private CheckBox _immichClearCredential = null!;
    private readonly TextBlock _status;

    public NextcloudDestinationSettings Nextcloud => _nextcloud.Clone();
    public ImmichDestinationSettings Immich => _immich.Clone();
    public string? NextcloudCredential { get; private set; }
    public bool RemoveNextcloudCredential { get; private set; }
    public string? ImmichCredential { get; private set; }
    public bool RemoveImmichCredential { get; private set; }

    public SelfHostedDestinationsWindow(SnaptureSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _nextcloud = (settings.Nextcloud ?? new()).Clone();
        _immich = (settings.Immich ?? new()).Clone();

        Title = "Snapture — Self-hosted destinations";
        Width = 760;
        Height = 650;
        MinWidth = 620;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = "Self-hosted destinations",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        root.Children.Add(heading);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 12, 0, 12) };
        var stack = new StackPanel();
        _nextcloudEnabled = new CheckBox { Content = "Enable Nextcloud destination", IsChecked = _nextcloud.Enabled };
        AutomationProperties.SetName(_nextcloudEnabled, "Enable Nextcloud destination");
        stack.Children.Add(BuildNextcloudPanel());
        _immichEnabled = new CheckBox { Content = "Enable Immich destination", IsChecked = _immich.Enabled };
        AutomationProperties.SetName(_immichEnabled, "Enable Immich destination");
        stack.Children.Add(BuildImmichPanel());
        var help = new TextBlock
        {
            Text = "Both connectors are disabled by default and never upload automatically. Nextcloud uses WebDAV with a user/app password; Immich uses its x-api-key header. Credentials are encrypted with Windows DPAPI and are not written to settings.json.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0)
        };
        help.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        stack.Children.Add(help);
        scroll.Content = stack;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = new DockPanel { LastChildFill = false };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 430, VerticalAlignment = VerticalAlignment.Center };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        DockPanel.SetDock(_status, Dock.Left);
        footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var save = new Button { Content = "Save", IsDefault = true };
        save.SetResourceReference(StyleProperty, "AccentButton");
        save.Click += OnSaveClicked;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
    }

    private Border BuildNextcloudPanel()
    {
        var stack = new StackPanel();
        var title = new TextBlock { Text = "Nextcloud WebDAV", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        stack.Children.Add(title);
        stack.Children.Add(_nextcloudEnabled);
        _nextcloudServer = AddTextField(stack, "Server URL", _nextcloud.ServerUrl, "https://cloud.example.test");
        _nextcloudUser = AddTextField(stack, "Username", _nextcloud.Username, "Nextcloud account name");
        _nextcloudFolder = AddTextField(stack, "Remote folder", _nextcloud.RemoteFolder, "Snapture");
        _nextcloudCredential = AddPasswordField(stack, "App password or password");
        _nextcloudClearCredential = new CheckBox { Content = "Remove the stored Nextcloud credential", Margin = new Thickness(170, 4, 0, 8) };
        stack.Children.Add(_nextcloudClearCredential);
        return WrapPanel(stack);
    }

    private Border BuildImmichPanel()
    {
        var stack = new StackPanel();
        var title = new TextBlock { Text = "Immich", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        stack.Children.Add(title);
        stack.Children.Add(_immichEnabled);
        _immichServer = AddTextField(stack, "Server URL", _immich.ServerUrl, "https://photos.example.test");
        _immichAlbum = AddTextField(stack, "Album ID (optional)", _immich.AlbumId, "UUID from Immich");
        _immichCredential = AddPasswordField(stack, "API key");
        _immichClearCredential = new CheckBox { Content = "Remove the stored Immich API key", Margin = new Thickness(170, 4, 0, 8) };
        stack.Children.Add(_immichClearCredential);
        return WrapPanel(stack);
    }

    private static Border WrapPanel(UIElement content) => new()
    {
        Padding = new Thickness(14),
        Margin = new Thickness(0, 0, 0, 12),
        Child = content,
        Style = Application.Current.TryFindResource("SectionPanel") as Style
    };

    private static TextBox AddTextField(Panel panel, string label, string value, string toolTip)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var box = new TextBox { Text = value, ToolTip = toolTip, MinHeight = 28 };
        AutomationProperties.SetName(box, label);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        panel.Children.Add(grid);
        return box;
    }

    private static PasswordBox AddPasswordField(Panel panel, string label)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var box = new PasswordBox { MinHeight = 28, ToolTip = "Leave blank to keep the currently stored credential." };
        AutomationProperties.SetName(box, label);
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        panel.Children.Add(grid);
        return box;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            _nextcloud.Enabled = _nextcloudEnabled.IsChecked == true;
            _nextcloud.ServerUrl = _nextcloudServer.Text.Trim();
            _nextcloud.Username = _nextcloudUser.Text.Trim();
            _nextcloud.RemoteFolder = _nextcloudFolder.Text.Trim();
            _immich.Enabled = _immichEnabled.IsChecked == true;
            _immich.ServerUrl = _immichServer.Text.Trim();
            _immich.AlbumId = _immichAlbum.Text.Trim();
            string? existingNextcloud = SelfHostedDestinationService.GetCredential(SelfHostedDestinationKind.Nextcloud);
            string? existingImmich = SelfHostedDestinationService.GetCredential(SelfHostedDestinationKind.Immich);
            string nextcloudCredential = _nextcloudCredential.Password;
            string immichCredential = _immichCredential.Password;
            if (_nextcloud.Enabled)
                SelfHostedDestinationService.ValidateNextcloud(_nextcloud, string.IsNullOrWhiteSpace(nextcloudCredential) ? existingNextcloud ?? string.Empty : nextcloudCredential);
            if (_immich.Enabled)
                SelfHostedDestinationService.ValidateImmich(_immich, string.IsNullOrWhiteSpace(immichCredential) ? existingImmich ?? string.Empty : immichCredential);
            NextcloudCredential = string.IsNullOrWhiteSpace(nextcloudCredential) ? null : nextcloudCredential;
            ImmichCredential = string.IsNullOrWhiteSpace(immichCredential) ? null : immichCredential;
            RemoveNextcloudCredential = _nextcloudClearCredential.IsChecked == true;
            RemoveImmichCredential = _immichClearCredential.IsChecked == true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }
}
