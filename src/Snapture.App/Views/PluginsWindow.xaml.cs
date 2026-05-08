using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class PluginsWindow : Window
{
    public PluginsWindow()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        PluginList.Children.Clear();
        var loader = App.Host?.Plugins;
        StatusText.Text = $"Plugins folder: {PluginLoader.PluginsDirectory}";
        if (loader is null) return;

        if (loader.All.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "No plugins installed. Drop any DLL that references Snapture.Plugin.Abstractions " +
                       "and is annotated with [SnapturePlugin] into the plugins folder, then click Reload.",
                Foreground = (Brush)FindResource("Subtext"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            PluginList.Children.Add(empty);
            return;
        }

        foreach (var p in loader.All)
        {
            var card = new Border
            {
                Background = (Brush)FindResource("Mantle"),
                BorderBrush = (Brush)FindResource("Surface0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
            };
            var stack = new StackPanel();
            var header = new TextBlock
            {
                Foreground = (Brush)FindResource("Mauve"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Text = $"{p.Info.Name}  ·  v{p.Info.Version}"
            };
            stack.Children.Add(header);
            stack.Children.Add(new TextBlock
            {
                Foreground = (Brush)FindResource("Subtext"),
                Text = $"by {p.Info.Author}",
                Margin = new Thickness(0, 2, 0, 6)
            });
            stack.Children.Add(new TextBlock
            {
                Foreground = (Brush)FindResource("Text"),
                Text = string.IsNullOrWhiteSpace(p.Info.Description) ? "(no description)" : p.Info.Description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            stack.Children.Add(new TextBlock
            {
                Foreground = (Brush)FindResource("Yellow"),
                Text = $"Capabilities: {p.Info.Capabilities}",
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });
            if (p.Info.ContractTypes.Count > 0)
            {
                stack.Children.Add(new TextBlock
                {
                    Foreground = (Brush)FindResource("Overlay1"),
                    Text = "Contributes: " + string.Join(", ", p.Info.ContractTypes),
                    FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            stack.Children.Add(new TextBlock
            {
                Foreground = (Brush)FindResource("Overlay1"),
                Text = p.Info.AssemblyPath,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0)
            });
            card.Child = stack;
            PluginList.Children.Add(card);
        }
    }

    private void OnReloadClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            App.Host?.Plugins.LoadAll();
            Refresh();
            StatusText.Text = $"Reloaded. {App.Host?.Plugins.All.Count ?? 0} plugins active.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Reload failed: {ex.Message}";
        }
    }

    private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(PluginLoader.PluginsDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", PluginLoader.PluginsDirectory) { UseShellExecute = true });
        }
        catch { }
    }
}
