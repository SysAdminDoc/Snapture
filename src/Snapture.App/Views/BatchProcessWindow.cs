using System.Windows;
using System.Windows.Controls;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Local folder-to-folder batch effect editor.</summary>
public sealed class BatchProcessWindow : Window
{
    private readonly TextBox _inputBox;
    private readonly TextBox _outputBox;
    private readonly TextBox _resizeBox;
    private readonly TextBox _borderBox;
    private readonly TextBox _watermarkBox;
    private readonly ComboBox _formatCombo;
    private readonly TextBlock _status;

    public BatchProcessWindow()
    {
        Title = "Snapture — Batch process";
        Width = 700;
        Height = 480;
        MinWidth = 620;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid { Margin = new Thickness(18) };
        for (int row = 0; row < 7; row++)
            root.RowDefinitions.Add(new RowDefinition { Height = row == 5 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var heading = new TextBlock { Text = "Batch process images", FontSize = 18, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        _inputBox = AddFolderRow(root, 1, "Input folder", "Choose a folder containing images", out var inputBrowse);
        _outputBox = AddFolderRow(root, 2, "Output folder", "Choose where processed images are written", out var outputBrowse);
        inputBrowse.Click += async (_, _) => await BrowseFolderAsync(_inputBox, "Choose a batch input folder");
        outputBrowse.Click += async (_, _) => await BrowseFolderAsync(_outputBox, "Choose a batch output folder");

        var effects = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        effects.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        effects.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        effects.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        effects.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        effects.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        effects.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _resizeBox = AddEffectField(effects, 0, 0, "Resize (%)", "100");
        _borderBox = AddEffectField(effects, 0, 2, "Border (px)", "0");
        _watermarkBox = AddEffectField(effects, 1, 0, "Watermark", "");
        var formatLabel = new TextBlock { Text = "Format", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0) };
        Grid.SetRow(formatLabel, 1);
        Grid.SetColumn(formatLabel, 2);
        effects.Children.Add(formatLabel);
        _formatCombo = new ComboBox { MinHeight = 28 };
        foreach (string format in new[] { "png", "jpg", "bmp", "webp" })
            _formatCombo.Items.Add(new ComboBoxItem { Content = format.ToUpperInvariant(), Tag = format });
        _formatCombo.SelectedIndex = 0;
        Grid.SetRow(_formatCombo, 1);
        Grid.SetColumn(_formatCombo, 3);
        effects.Children.Add(_formatCombo);
        Grid.SetRow(effects, 3);
        root.Children.Add(effects);

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        Grid.SetRow(_status, 4);
        root.Children.Add(_status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var run = new Button { Content = "Process", IsDefault = true, MinWidth = 100 };
        run.SetResourceReference(StyleProperty, "AccentButton");
        run.Click += OnRunClicked;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(8, 0, 0, 0), MinWidth = 90 };
        cancel.Click += (_, _) => Close();
        actions.Children.Add(run);
        actions.Children.Add(cancel);
        Grid.SetRow(actions, 6);
        root.Children.Add(actions);
        Content = root;
    }

    private static TextBox AddEffectField(Grid grid, int row, int column, string label, string value)
    {
        var labelBlock = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(column == 0 ? 0 : 12, 0, 8, 0) };
        Grid.SetRow(labelBlock, row);
        Grid.SetColumn(labelBlock, column);
        grid.Children.Add(labelBlock);
        var box = new TextBox { Text = value, MinHeight = 28 };
        Grid.SetRow(box, row);
        Grid.SetColumn(box, column + 1);
        grid.Children.Add(box);
        return box;
    }

    private static TextBox AddFolderRow(Grid root, int row, string label, string title, out Button browse)
    {
        var panel = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var box = new TextBox { MinHeight = 28 };
        Grid.SetColumn(box, 1);
        panel.Children.Add(box);
        browse = new Button { Content = "Browse…", ToolTip = title, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(browse, 2);
        panel.Children.Add(browse);
        Grid.SetRow(panel, row);
        root.Children.Add(panel);
        return box;
    }

    private async Task BrowseFolderAsync(TextBox target, string title)
    {
        var path = await StoragePickerService.PickFolderAsync(this, target.Text, title);
        if (path is not null)
            target.Text = path;
    }

    private void OnRunClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            int resize = int.TryParse(_resizeBox.Text, out var parsedResize) ? parsedResize : 100;
            int border = int.TryParse(_borderBox.Text, out var parsedBorder) ? parsedBorder : 0;
            string format = (_formatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "png";
            var results = BatchProcessService.ProcessDirectory(
                _inputBox.Text,
                _outputBox.Text,
                new BatchProcessOptions(resize, border, WatermarkText: _watermarkBox.Text.Trim(), OutputFormat: format));
            int succeeded = results.Count(result => result.Succeeded);
            int failed = results.Count - succeeded;
            _status.Text = failed == 0
                ? $"Processed {succeeded} image(s)."
                : $"Processed {succeeded} image(s); {failed} failed. Check the source files and try again.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }
}
