using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Per-export override for the three independent metadata decisions.</summary>
public sealed class ExportMetadataDialog : Window
{
    private readonly ComboBox _metadataCombo;
    private readonly ComboBox _iccCombo;
    private readonly ComboBox _provenanceCombo;

    public ExportMetadataOptions Options { get; private set; }

    public ExportMetadataDialog(ExportMetadataOptions initial)
    {
        Options = initial;
        Title = "Export metadata policy";
        Width = 560;
        Height = 470;
        MinWidth = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Choose what this export carries",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AppForeground");
        root.Children.Add(title);

        var choices = new StackPanel { Margin = new Thickness(0, 12, 0, 18) };
        _metadataCombo = AddChoice(
            choices,
            "Ordinary source metadata",
            "EXIF, XMP, comments, and similar source fields. Redacted exports suppress source fields even when preservation is selected.",
            new[]
            {
                (ExportMetadataMode.Strip, "Strip source metadata (default)"),
                (ExportMetadataMode.PreserveSource, "Preserve source metadata"),
                (ExportMetadataMode.ReplaceWithSnapture, "Replace with Snapture metadata")
            },
            initial.Metadata);
        _iccCombo = AddChoice(
            choices,
            "ICC profile",
            "Display ICC is separate from ordinary metadata. A composite capture has no single display profile to embed.",
            new[]
            {
                (ExportIccMode.Strip, "Strip ICC data"),
                (ExportIccMode.PreserveSource, "Preserve source ICC"),
                (ExportIccMode.EmbedDisplay, "Embed display ICC when available (default)")
            },
            initial.Icc);
        _provenanceCombo = AddChoice(
            choices,
            "Provenance",
            "The optional sidecar is descriptive local metadata, not a signed C2PA authenticity assertion.",
            new[]
            {
                (ExportProvenanceMode.Disabled, "Disabled (default)"),
                (ExportProvenanceMode.Sidecar, "Write .provenance.json sidecar")
            },
            initial.Provenance);
        Grid.SetRow(choices, 1);
        root.Children.Add(choices);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button { Content = "Cancel", MinWidth = 96, Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; };
        var export = new Button { Content = "Export", MinWidth = 96, IsDefault = true };
        export.Click += OnExportClicked;
        buttons.Children.Add(cancel);
        buttons.Children.Add(export);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
    }

    private static ComboBox AddChoice<T>(
        Panel parent,
        string title,
        string description,
        IReadOnlyList<(T Value, string Label)> items,
        T selected)
        where T : struct, Enum
    {
        var label = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 3)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AppForeground");
        parent.Children.Add(label);

        var help = new TextBlock
        {
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 6)
        };
        help.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        parent.Children.Add(help);

        var combo = new ComboBox { MinWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
        foreach (var item in items)
            combo.Items.Add(new ComboBoxItem { Tag = item.Value.ToString(), Content = item.Label });
        combo.SelectedIndex = Array.FindIndex(items.ToArray(), item => EqualityComparer<T>.Default.Equals(item.Value, selected));
        if (combo.SelectedIndex < 0) combo.SelectedIndex = 0;
        parent.Children.Add(combo);
        return combo;
    }

    private void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        Options = new ExportMetadataOptions(
            ReadEnum(_metadataCombo, ExportMetadataMode.Strip),
            ReadEnum(_iccCombo, ExportIccMode.EmbedDisplay),
            ReadEnum(_provenanceCombo, ExportProvenanceMode.Disabled));
        DialogResult = true;
    }

    private static T ReadEnum<T>(ComboBox combo, T fallback)
        where T : struct, Enum
    {
        string? tag = (combo.SelectedItem as ComboBoxItem)?.Tag as string;
        return Enum.TryParse(tag, ignoreCase: true, out T value)
            && Enum.IsDefined(typeof(T), value)
            ? value
            : fallback;
    }
}
