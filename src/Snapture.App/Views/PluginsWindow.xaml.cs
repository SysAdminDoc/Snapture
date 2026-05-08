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
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            empty.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
            PluginList.Children.Add(empty);
            return;
        }

        foreach (var p in loader.All)
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 0, 10),
            };
            card.SetResourceReference(Border.BackgroundProperty, "AppSurface");
            card.SetResourceReference(Border.BorderBrushProperty, "AppBorder");
            var stack = new StackPanel();
            var header = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                FontSize = 15,
                Text = $"{p.Info.Name}  ·  v{p.Info.Version}"
            };
            header.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
            stack.Children.Add(header);
            var author = new TextBlock
            {
                Text = $"by {p.Info.Author}",
                Margin = new Thickness(0, 2, 0, 6)
            };
            author.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
            stack.Children.Add(author);
            var description = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(p.Info.Description) ? "(no description)" : p.Info.Description,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            description.SetResourceReference(TextBlock.ForegroundProperty, "AppForeground");
            stack.Children.Add(description);
            var capabilities = new TextBlock
            {
                Text = $"Capabilities: {p.Info.Capabilities}",
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };
            capabilities.SetResourceReference(TextBlock.ForegroundProperty, "AppWarning");
            stack.Children.Add(capabilities);
            if (p.Info.ContractTypes.Count > 0)
            {
                var contributes = new TextBlock
                {
                    Text = "Contributes: " + string.Join(", ", p.Info.ContractTypes),
                    FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                };
                contributes.SetResourceReference(TextBlock.ForegroundProperty, "AppSubtleForeground");
                stack.Children.Add(contributes);
            }
            var path = new TextBlock
            {
                Text = p.Info.AssemblyPath,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 6, 0, 0)
            };
            path.SetResourceReference(TextBlock.ForegroundProperty, "AppSubtleForeground");
            stack.Children.Add(path);
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
