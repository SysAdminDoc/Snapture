using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Chooses one enabled first-party self-hosted destination.</summary>
public sealed class SelfHostedDestinationPickerWindow : Window
{
    private readonly ListBox _destinations;

    public SelfHostedDestinationKind? SelectedDestination { get; private set; }

    public SelfHostedDestinationPickerWindow(IEnumerable<SelfHostedDestinationKind> destinations)
    {
        var choices = destinations.ToList();
        Title = "Snapture — Self-hosted destination";
        Width = 460;
        Height = 320;
        MinWidth = 360;
        MinHeight = 240;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var description = new TextBlock
        {
            Text = "Choose an enabled destination. The current editor image is flattened to PNG before upload.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        root.Children.Add(description);

        _destinations = new ListBox { Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(_destinations, "Self-hosted destination choices");
        foreach (var destination in choices)
            _destinations.Items.Add(new ListBoxItem
            {
                Content = destination == SelfHostedDestinationKind.Nextcloud ? "Nextcloud WebDAV" : "Immich",
                Tag = destination
            });
        _destinations.SelectedIndex = choices.Count == 1 ? 0 : -1;
        _destinations.MouseDoubleClick += (_, _) => Accept();
        Grid.SetRow(_destinations, 1);
        root.Children.Add(_destinations);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var upload = new Button { Content = "Upload", IsDefault = true };
        upload.SetResourceReference(StyleProperty, "AccentButton");
        upload.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(upload);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    private void Accept()
    {
        if (_destinations.SelectedItem is not ListBoxItem { Tag: SelfHostedDestinationKind destination })
            return;
        SelectedDestination = destination;
        DialogResult = true;
        Close();
    }
}
