using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Chooses local stills and combines them into a vertical, horizontal, or grid image.</summary>
public sealed class ImageCombinerWindow : Window
{
    private readonly ListBox _files;
    private readonly ComboBox _layoutCombo;
    private readonly TextBox _gapBox;
    private readonly TextBox _columnsBox;
    private readonly ComboBox _formatCombo;
    private readonly TextBlock _status;

    public ImageCombinerWindow()
    {
        Title = "Snapture — Image combiner";
        Width = 720;
        Height = 540;
        MinWidth = 580;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Combine images",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        root.Children.Add(heading);

        var helper = new TextBlock
        {
            Text = "Add at least two local images. Sources are decoded before the output is written, so a source cannot be overwritten accidentally.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        };
        helper.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        Grid.SetRow(helper, 1);
        root.Children.Add(helper);

        _files = new ListBox { AllowDrop = false, Margin = new Thickness(0, 0, 0, 10) };
        _files.SelectionMode = SelectionMode.Extended;
        _files.DisplayMemberPath = "Name";
        Grid.SetRow(_files, 2);
        root.Children.Add(_files);

        var fileActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        var add = new Button { Content = "Add images…", MinWidth = 110 };
        add.Click += (_, _) => AddImages();
        var remove = new Button { Content = "Remove selected", MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) => RemoveSelected();
        fileActions.Children.Add(add);
        fileActions.Children.Add(remove);
        Grid.SetRow(fileActions, 3);
        root.Children.Add(fileActions);

        var options = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        options.Children.Add(new TextBlock { Text = "Layout", VerticalAlignment = VerticalAlignment.Center });
        _layoutCombo = new ComboBox { MinHeight = 28 };
        AddLayout("Vertical", ImageCombineLayout.Vertical);
        AddLayout("Horizontal", ImageCombineLayout.Horizontal);
        AddLayout("Grid", ImageCombineLayout.Grid);
        _layoutCombo.SelectedIndex = 0;
        Grid.SetColumn(_layoutCombo, 1);
        options.Children.Add(_layoutCombo);
        options.Children.Add(new TextBlock { Text = "Gap (px)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        Grid.SetColumn(options.Children[^1], 2);
        _gapBox = new TextBox { Text = "16", MinHeight = 28 };
        Grid.SetColumn(_gapBox, 3);
        options.Children.Add(_gapBox);
        options.Children.Add(new TextBlock { Text = "Columns", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        Grid.SetColumn(options.Children[^1], 4);
        _columnsBox = new TextBox { Text = "2", MinHeight = 28 };
        Grid.SetColumn(_columnsBox, 5);
        options.Children.Add(_columnsBox);
        options.Children.Add(new TextBlock { Text = "Format", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        Grid.SetColumn(options.Children[^1], 6);
        _formatCombo = new ComboBox { MinHeight = 28 };
        foreach (string format in new[] { "png", "jpg", "bmp", "webp" })
            _formatCombo.Items.Add(new ComboBoxItem { Content = format.ToUpperInvariant(), Tag = format });
        _formatCombo.SelectedIndex = 0;
        Grid.SetColumn(_formatCombo, 7);
        options.Children.Add(_formatCombo);
        Grid.SetRow(options, 4);
        root.Children.Add(options);

        var bottom = new Grid();
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420 };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        bottom.Children.Add(_status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var combine = new Button { Content = "Combine…", IsDefault = true, MinWidth = 105 };
        combine.SetResourceReference(StyleProperty, "AccentButton");
        combine.Click += async (_, _) => await CombineAsync();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 85, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        actions.Children.Add(combine);
        actions.Children.Add(cancel);
        Grid.SetColumn(actions, 1);
        bottom.Children.Add(actions);
        Grid.SetRow(bottom, 5);
        root.Children.Add(bottom);
        Content = root;

        void AddLayout(string label, ImageCombineLayout layout) =>
            _layoutCombo.Items.Add(new ComboBoxItem { Content = label, Tag = layout });
    }

    private void AddImages()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff",
            Multiselect = true,
            CheckFileExists = true,
            Title = "Choose images to combine"
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var existing = _files.Items.Cast<FileChoice>().Select(item => item.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string path in dialog.FileNames)
        {
            if (existing.Add(path))
                _files.Items.Add(new FileChoice(Path.GetFileName(path), path));
        }
        _status.Text = $"{_files.Items.Count} image(s) selected.";
    }

    private void RemoveSelected()
    {
        var selected = _files.SelectedItems.Cast<object>().ToArray();
        foreach (var item in selected)
            _files.Items.Remove(item);
        _status.Text = $"{_files.Items.Count} image(s) selected.";
    }

    private async Task CombineAsync()
    {
        var inputs = _files.Items.Cast<FileChoice>().Select(item => item.Path).ToArray();
        if (inputs.Length < 2)
        {
            _status.Text = "Choose at least two images.";
            return;
        }

        string format = (_formatCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "png";
        var output = await StoragePickerService.PickSaveFileAsync(
            this,
            "PNG image (*.png)|*.png|JPEG image (*.jpg)|*.jpg|Bitmap image (*.bmp)|*.bmp|WebP image (*.webp)|*.webp",
            "combined-snapture.png",
            format,
            new[]
            {
                new StoragePickerService.FileTypeChoice("PNG image", new[] { ".png" }),
                new StoragePickerService.FileTypeChoice("JPEG image", new[] { ".jpg" }),
                new StoragePickerService.FileTypeChoice("Bitmap image", new[] { ".bmp" }),
                new StoragePickerService.FileTypeChoice("WebP image", new[] { ".webp" })
            },
            title: "Save combined image");
        if (output is null)
            return;
        if (ImageConversionService.TryNormalizeFormat(Path.GetExtension(output), out var outputFormat))
            format = outputFormat;

        if (!int.TryParse(_gapBox.Text, out int gap))
            gap = 16;
        if (!int.TryParse(_columnsBox.Text, out int columns))
            columns = 2;
        var layout = (_layoutCombo.SelectedItem as ComboBoxItem)?.Tag is ImageCombineLayout selectedLayout
            ? selectedLayout
            : ImageCombineLayout.Vertical;
        try
        {
            var result = await Task.Run(() => ImageCombinerService.Combine(
                inputs,
                output,
                new ImageCombinerOptions(layout, gap, GridColumns: columns, OutputFormat: format)));
            _status.Text = $"Combined {result.ImageCount} image(s) at {result.Width} × {result.Height}: {result.OutputPath}";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private sealed record FileChoice(string Name, string Path);
}
