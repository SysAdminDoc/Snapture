using System.Windows;
using System.Windows.Controls;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class OcrTableResultWindow : Window
{
    private readonly OcrTableResult _table;

    public OcrTableResultWindow(OcrTableResult table)
    {
        InitializeComponent();
        _table = table ?? throw new ArgumentNullException(nameof(table));
        CountText.Text = $"{_table.Rows.Count} rows · {_table.ColumnCount} columns";
        StatusText.Text = _table.IsEmpty
            ? "No positioned table text."
            : $"{_table.Engine} · Copy as tab-separated values.";

        if (_table.IsEmpty)
        {
            EmptyState.Visibility = Visibility.Visible;
            CopyButton.IsEnabled = false;
            return;
        }

        BuildTable();
    }

    private void BuildTable()
    {
        for (var column = 0; column < _table.ColumnCount; column++)
            TableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });

        AddHeaderRow();
        for (var row = 0; row < _table.Rows.Count; row++)
        {
            TableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var column = 0; column < _table.ColumnCount; column++)
                AddCell(row + 1, column, _table.Rows[row].Cells[column].Text, isHeader: false);
        }
    }

    private void AddHeaderRow()
    {
        TableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var column = 0; column < _table.ColumnCount; column++)
            AddCell(0, column, $"Column {column + 1}", isHeader: true);
    }

    private void AddCell(int row, int column, string value, bool isHeader)
    {
        var border = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 1, 1),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = value,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 20
            }
        };
        border.SetResourceReference(Border.BackgroundProperty, isHeader ? "AppSurfaceRaised" : "AppSurface");
        border.SetResourceReference(Border.BorderBrushProperty, "AppBorder");
        var text = (TextBlock)border.Child;
        text.SetResourceReference(TextBlock.ForegroundProperty, isHeader ? "AppAccent" : "AppForeground");
        if (isHeader) text.FontWeight = FontWeights.SemiBold;
        Grid.SetRow(border, row);
        Grid.SetColumn(border, column);
        TableGrid.Children.Add(border);
    }

    private void OnCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(OcrTableBuilder.ToTsv(_table));
            StatusText.Text = "Copied TSV to clipboard.";
        }
        catch
        {
            StatusText.Text = "Clipboard busy — try again.";
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}

