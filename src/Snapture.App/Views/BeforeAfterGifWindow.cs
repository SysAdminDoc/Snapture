using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Creates a local ping-pong comparison GIF from two still images.</summary>
public sealed class BeforeAfterGifWindow : Window
{
    private readonly TextBox _beforeBox;
    private readonly TextBox _afterBox;
    private readonly TextBox _framesBox;
    private readonly TextBox _delayBox;
    private readonly TextBlock _status;

    public BeforeAfterGifWindow()
    {
        Title = "Snapture — Before/after GIF";
        Width = 650;
        Height = 380;
        MinWidth = 540;
        MinHeight = 330;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid { Margin = new Thickness(18) };
        for (int row = 0; row < 6; row++)
            root.RowDefinitions.Add(new RowDefinition { Height = row == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var heading = new TextBlock { Text = "Before / after comparison GIF", FontSize = 18, FontWeight = FontWeights.SemiBold };
        heading.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        root.Children.Add(heading);

        _beforeBox = AddFileRow(root, 1, "Before image", "Choose the starting still", out var beforeBrowse);
        _afterBox = AddFileRow(root, 2, "After image", "Choose the ending still", out var afterBrowse);
        beforeBrowse.Click += (_, _) => ChooseImage(_beforeBox, "Choose before image");
        afterBrowse.Click += (_, _) => ChooseImage(_afterBox, "Choose after image");

        var options = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        options.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        options.Children.Add(new TextBlock { Text = "Transition frames", VerticalAlignment = VerticalAlignment.Center });
        _framesBox = new TextBox { Text = "12", MinHeight = 28 };
        Grid.SetColumn(_framesBox, 1);
        options.Children.Add(_framesBox);
        options.Children.Add(new TextBlock { Text = "Frame delay (ms)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 8, 0) });
        Grid.SetColumn(options.Children[^1], 2);
        _delayBox = new TextBox { Text = "100", MinHeight = 28 };
        Grid.SetColumn(_delayBox, 3);
        options.Children.Add(_delayBox);
        Grid.SetRow(options, 3);
        root.Children.Add(options);

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        Grid.SetRow(_status, 4);
        root.Children.Add(_status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var create = new Button { Content = "Create GIF…", IsDefault = true, MinWidth = 110 };
        create.SetResourceReference(StyleProperty, "AccentButton");
        create.Click += async (_, _) => await CreateAsync();
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 85, Margin = new Thickness(8, 0, 0, 0) };
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(create);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);
        Content = root;
    }

    private static TextBox AddFileRow(Grid root, int row, string label, string title, out Button browse)
    {
        var panel = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
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

    private static void ChooseImage(TextBox target, string title)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff",
            CheckFileExists = true,
            Title = title
        };
        if (dialog.ShowDialog() == true)
            target.Text = dialog.FileName;
    }

    private async Task CreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_beforeBox.Text) || string.IsNullOrWhiteSpace(_afterBox.Text))
        {
            _status.Text = "Choose both a before and an after image.";
            return;
        }

        var output = await StoragePickerService.PickSaveFileAsync(
            this,
            "GIF image (*.gif)|*.gif",
            "before-after-snapture.gif",
            ".gif",
            new[] { new StoragePickerService.FileTypeChoice("GIF image", new[] { ".gif" }) },
            title: "Save before/after GIF");
        if (output is null)
            return;

        int frames = int.TryParse(_framesBox.Text, out var parsedFrames) ? parsedFrames : 12;
        int delay = int.TryParse(_delayBox.Text, out var parsedDelay) ? parsedDelay : 100;
        try
        {
            var result = await Task.Run(() => BeforeAfterGifService.CreateGif(
                _beforeBox.Text,
                _afterBox.Text,
                output,
                new BeforeAfterGifOptions(frames, delay)));
            _status.Text = $"Created {result.FrameCount}-frame GIF at {result.Width} × {result.Height}: {result.OutputPath}";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }
}
