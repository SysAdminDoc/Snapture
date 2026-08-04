using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Snapture.App.Services;

namespace Snapture.App.Views;

/// <summary>Snap-arranges the currently selected pinned images into a comparison board.</summary>
public sealed class PinBoardWindow : Window
{
    private readonly IReadOnlyList<BitmapSource> _images;
    private readonly Canvas _board;
    private readonly ComboBox _layoutCombo;
    private readonly TextBox _gapBox;
    private readonly TextBox _columnsBox;
    private readonly TextBox _nameBox;
    private readonly ComboBox _savedCombo;
    private readonly TextBlock _status;
    private readonly PinBoardLayoutStore _store;

    public PinBoardWindow(IReadOnlyList<BitmapSource> images)
    {
        ArgumentNullException.ThrowIfNull(images);
        if (images.Count == 0)
            throw new ArgumentException("At least one pin is required.", nameof(images));
        _images = images.ToArray();
        _store = new PinBoardLayoutStore(PortableMode.LocalDataDirectory);

        Title = "Snapture — Pin comparison board";
        Width = 1_100;
        Height = 760;
        MinWidth = 680;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10), VerticalAlignment = VerticalAlignment.Center };
        toolbar.Children.Add(new TextBlock { Text = "Layout", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        _layoutCombo = new ComboBox { Width = 110, MinHeight = 28 };
        AddLayout("Grid", PinBoardLayoutKind.Grid);
        AddLayout("Vertical", PinBoardLayoutKind.Vertical);
        AddLayout("Horizontal", PinBoardLayoutKind.Horizontal);
        _layoutCombo.SelectedIndex = 0;
        toolbar.Children.Add(_layoutCombo);
        toolbar.Children.Add(new TextBlock { Text = "Gap", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        _gapBox = new TextBox { Text = "16", Width = 55, MinHeight = 28 };
        toolbar.Children.Add(_gapBox);
        toolbar.Children.Add(new TextBlock { Text = "Columns", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        _columnsBox = new TextBox { Text = "2", Width = 55, MinHeight = 28 };
        toolbar.Children.Add(_columnsBox);
        var apply = new Button { Content = "Arrange", MinWidth = 80, Margin = new Thickness(14, 0, 0, 0) };
        apply.SetResourceReference(StyleProperty, "AccentButton");
        apply.Click += (_, _) => ApplyLayout();
        toolbar.Children.Add(apply);
        toolbar.Children.Add(new TextBlock { Text = "Name", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 6, 0) });
        _nameBox = new TextBox { Text = "comparison", Width = 105, MinHeight = 28 };
        toolbar.Children.Add(_nameBox);
        var save = new Button { Content = "Save layout", MinWidth = 90, Margin = new Thickness(6, 0, 0, 0) };
        save.Click += (_, _) => SaveLayout();
        toolbar.Children.Add(save);
        _savedCombo = new ComboBox
        {
            Width = 120,
            MinHeight = 28,
            Margin = new Thickness(14, 0, 0, 0),
            DisplayMemberPath = "Name"
        };
        toolbar.Children.Add(_savedCombo);
        var load = new Button { Content = "Load", MinWidth = 62, Margin = new Thickness(6, 0, 0, 0) };
        load.Click += (_, _) => LoadLayout();
        toolbar.Children.Add(load);
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = Application.Current?.TryFindResource("AppCanvas") as System.Windows.Media.Brush
        };
        _board = new Canvas { Margin = new Thickness(30) };
        scroll.Content = _board;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
        _status.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        Grid.SetRow(_status, 2);
        root.Children.Add(_status);
        Content = root;

        RefreshSavedLayouts();
        ApplyLayout();

        void AddLayout(string label, PinBoardLayoutKind layout) =>
            _layoutCombo.Items.Add(new ComboBoxItem { Content = label, Tag = layout });
    }

    private void ApplyLayout()
    {
        try
        {
            int gap = int.TryParse(_gapBox.Text, out var parsedGap) ? parsedGap : 16;
            int columns = int.TryParse(_columnsBox.Text, out var parsedColumns) ? parsedColumns : 2;
            var layout = (_layoutCombo.SelectedItem as ComboBoxItem)?.Tag is PinBoardLayoutKind selected
                ? selected
                : PinBoardLayoutKind.Grid;
            var arrangement = PinBoardLayoutService.Arrange(
                _images.Select(image => new System.Drawing.Size(image.PixelWidth, image.PixelHeight)).ToArray(),
                new PinBoardLayoutOptions(layout, gap, columns));
            _board.Children.Clear();
            _board.Width = arrangement.Width;
            _board.Height = arrangement.Height;
            foreach (var placement in arrangement.Placements)
            {
                var image = new System.Windows.Controls.Image
                {
                    Source = _images[placement.Index],
                    Width = placement.Bounds.Width,
                    Height = placement.Bounds.Height,
                    Stretch = System.Windows.Media.Stretch.None,
                    SnapsToDevicePixels = true
                };
                Canvas.SetLeft(image, placement.Bounds.X);
                Canvas.SetTop(image, placement.Bounds.Y);
                _board.Children.Add(image);
            }
            _status.Text = $"{_images.Count} pin(s) arranged at {arrangement.Width} × {arrangement.Height}. Layouts save arrangement settings only; pixels remain in the pin windows.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void SaveLayout()
    {
        try
        {
            _store.Save(_nameBox.Text, ReadOptions());
            RefreshSavedLayouts(_nameBox.Text.Trim());
            _status.Text = $"Saved layout '{_nameBox.Text.Trim()}'.";
        }
        catch (Exception ex)
        {
            _status.Text = ex.Message;
        }
    }

    private void LoadLayout()
    {
        if (_savedCombo.SelectedItem is not PinBoardSavedLayout saved)
            return;
        _nameBox.Text = saved.Name;
        _layoutCombo.SelectedIndex = _layoutCombo.Items.Cast<ComboBoxItem>().ToList().FindIndex(item => Equals(item.Tag, saved.Options.Layout));
        _gapBox.Text = saved.Options.Gap.ToString();
        _columnsBox.Text = saved.Options.GridColumns.ToString();
        ApplyLayout();
    }

    private PinBoardLayoutOptions ReadOptions()
    {
        int gap = int.TryParse(_gapBox.Text, out var parsedGap) ? parsedGap : 16;
        int columns = int.TryParse(_columnsBox.Text, out var parsedColumns) ? parsedColumns : 2;
        var layout = (_layoutCombo.SelectedItem as ComboBoxItem)?.Tag is PinBoardLayoutKind selected
            ? selected
            : PinBoardLayoutKind.Grid;
        return new PinBoardLayoutOptions(layout, gap, columns);
    }

    private void RefreshSavedLayouts(string? selectName = null)
    {
        _savedCombo.Items.Clear();
        var layouts = _store.Load();
        foreach (var layout in layouts)
            _savedCombo.Items.Add(layout);
        if (selectName is not null)
            _savedCombo.SelectedItem = layouts.FirstOrDefault(layout => string.Equals(layout.Name, selectName, StringComparison.OrdinalIgnoreCase));
        else if (_savedCombo.Items.Count > 0)
            _savedCombo.SelectedIndex = 0;
    }
}
