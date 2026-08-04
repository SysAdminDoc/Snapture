using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Chooses one configured external command for the current editor document.</summary>
public sealed class ExternalCommandPickerWindow : Window
{
    private readonly ListBox _profiles;

    public ExternalCommandProfile? SelectedProfile { get; private set; }

    public ExternalCommandPickerWindow(IEnumerable<ExternalCommandProfile> profiles)
    {
        var choices = profiles.Select(profile => profile.Clone()).ToList();
        Title = "Snapture — Run external command";
        Width = 480;
        Height = 340;
        MinWidth = 380;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var description = new TextBlock
        {
            Text = "Choose a configured command. The editor image is flattened to PNG before it is sent.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        description.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        root.Children.Add(description);

        _profiles = new ListBox { Margin = new Thickness(0, 0, 0, 12) };
        AutomationProperties.SetName(_profiles, "External command choices");
        foreach (var profile in choices)
            _profiles.Items.Add(new ListBoxItem { Content = profile, Tag = profile });
        _profiles.SelectedIndex = choices.Count == 1 ? 0 : -1;
        _profiles.MouseDoubleClick += (_, _) => Accept();
        Grid.SetRow(_profiles, 1);
        root.Children.Add(_profiles);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var run = new Button { Content = "Run", IsDefault = true };
        run.SetResourceReference(StyleProperty, "AccentButton");
        run.Click += (_, _) => Accept();
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        buttons.Children.Add(run);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    private void Accept()
    {
        if (_profiles.SelectedItem is not ListBoxItem { Tag: ExternalCommandProfile profile })
            return;
        SelectedProfile = profile.Clone();
        DialogResult = true;
        Close();
    }
}
