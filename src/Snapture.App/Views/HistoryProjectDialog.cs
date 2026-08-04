using System.Windows;
using System.Windows.Controls;

namespace Snapture.App.Views;

internal sealed class HistoryProjectDialog : Window
{
    private readonly TextBox _nameBox;

    public string ProjectName => _nameBox.Text.Trim();

    public HistoryProjectDialog()
    {
        Title = "New history project";
        Width = 420;
        Height = 210;
        MinWidth = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "AppBackground");
        SetResourceReference(ForegroundProperty, "AppForeground");

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Project name",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
        Grid.SetRow(title, 0);

        var helper = new TextBlock
        {
            Text = "Group related captures together without moving their image files.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        helper.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
        Grid.SetRow(helper, 1);

        _nameBox = new TextBox
        {
            MinHeight = 36,
            MaxLength = 80,
            ToolTip = "Use a short name such as Release notes or Incident 2026-08-03."
        };
        Grid.SetRow(_nameBox, 2);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var create = new Button
        {
            Content = "Create project",
            Width = 112,
            IsDefault = true
        };
        create.SetResourceReference(StyleProperty, "AccentButton");
        create.Click += (_, _) =>
        {
            if (ProjectName.Length > 0)
                DialogResult = true;
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 80,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true
        };
        cancel.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(create);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 3);

        grid.Children.Add(title);
        grid.Children.Add(helper);
        grid.Children.Add(_nameBox);
        grid.Children.Add(buttons);
        Content = grid;
        Loaded += (_, _) => _nameBox.Focus();
    }
}
