using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapture.Plugin;

namespace Snapture.App.Views;

/// <summary>Small host-rendered JSON editor for optional plugin configuration contracts.</summary>
public sealed class PluginConfigurationWindow : Window
{
    private readonly List<(IPluginConfigurable Plugin, TextBox Editor)> _editors = new();
    private readonly TextBlock _status;

    public PluginConfigurationWindow(string pluginName, IReadOnlyList<IPluginConfigurable> configurables)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(configurables);
        if (configurables.Count == 0) throw new ArgumentException("No configurable plugin was supplied.", nameof(configurables));

        Title = $"Snapture — Configure {pluginName}";
        Width = 760;
        Height = 560;
        MinWidth = 540;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Plugin configuration",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(18, 16, 18, 4)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        root.Children.Add(heading);

        var tabs = new TabControl { Margin = new Thickness(18, 8, 18, 8) };
        foreach (var configurable in configurables)
        {
            var editor = new TextBox
            {
                Text = configurable.ConfigurationJson,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                FontSize = 12,
                Padding = new Thickness(10)
            };
            editor.SetResourceReference(TextBox.BackgroundProperty, "AppCanvas");
            editor.SetResourceReference(TextBox.ForegroundProperty, "AppForeground");
            editor.SetResourceReference(TextBox.BorderBrushProperty, "AppBorder");
            _editors.Add((configurable, editor));
            tabs.Items.Add(new TabItem
            {
                Header = string.IsNullOrWhiteSpace(configurable.ConfigurationTitle)
                    ? "Configuration"
                    : configurable.ConfigurationTitle,
                Content = editor
            });
        }
        Grid.SetRow(tabs, 1);
        root.Children.Add(tabs);

        var footer = new DockPanel { Margin = new Thickness(18, 0, 18, 14), LastChildFill = false };
        _status = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 460
        };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        DockPanel.SetDock(_status, Dock.Left);
        footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true };
        cancel.Click += (_, _) => Close();
        var save = new Button { Content = "Save", IsDefault = true };
        save.SetResourceReference(Button.StyleProperty, "AccentButton");
        save.Click += OnSaveClicked;
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var (plugin, editor) in _editors)
            {
                using var document = JsonDocument.Parse(editor.Text);
                plugin.ApplyConfigurationJson(document.RootElement.GetRawText());
            }
            DialogResult = true;
            Close();
        }
        catch (JsonException ex)
        {
            _status.Text = $"Configuration must be valid JSON: {ex.Message}";
        }
        catch (Exception ex)
        {
            _status.Text = $"Could not save configuration: {ex.Message}";
        }
    }
}
