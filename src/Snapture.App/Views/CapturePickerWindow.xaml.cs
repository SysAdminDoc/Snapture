using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snapture.App.Views;

public partial class CapturePickerWindow : Window
{
    public enum CaptureMode
    {
        Region, Window, Fullscreen, LastRegion, ScrollingWindow, SmartElement, MonitorUnderCursor
    }

    private static readonly (CaptureMode mode, string label, string description, string glyph, string hotkey)[] Modes =
    {
        (CaptureMode.Region,             "Region",                    "Draw an exact area on screen.",          "⊞", "1"),
        (CaptureMode.Window,             "Foreground window",         "Capture the active app window.",         "⊟", "2"),
        (CaptureMode.Fullscreen,         "Fullscreen",                "Capture every monitor at once.",         "⊠", "3"),
        (CaptureMode.MonitorUnderCursor, "Monitor under cursor",      "Use the display beneath the pointer.",   "▣", "4"),
        (CaptureMode.LastRegion,         "Last region",               "Repeat the previous region bounds.",     "↺", "5"),
        (CaptureMode.ScrollingWindow,    "Scrolling window",          "Stitch a long scrollable surface.",      "⇕", "6"),
        (CaptureMode.SmartElement,       "Smart element",             "Snap to a UI Automation element.",       "◎", "7"),
    };

    public CaptureMode? SelectedMode { get; private set; }

    public CapturePickerWindow()
    {
        InitializeComponent();

        foreach (var (mode, label, description, glyph, hotkey) in Modes)
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var icon = new TextBlock
            {
                Text = glyph,
                FontSize = 17,
                Width = 28,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AppAccent");
            Grid.SetColumn(icon, 0);
            content.Children.Add(icon);

            var copy = new StackPanel { Orientation = Orientation.Vertical };
            copy.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            var desc = new TextBlock
            {
                Text = description,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0)
            };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "AppMutedForeground");
            copy.Children.Add(desc);
            Grid.SetColumn(copy, 1);
            content.Children.Add(copy);

            var key = new TextBlock
            {
                Text = hotkey,
                FontSize = 11,
                FontFamily = new FontFamily("Cascadia Code, Consolas, monospace"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(14, 0, 0, 0)
            };
            key.SetResourceReference(TextBlock.ForegroundProperty, "AppSubtleForeground");
            Grid.SetColumn(key, 2);
            content.Children.Add(key);

            var btn = new Button
            {
                Content = content,
                Margin = new Thickness(0, 2, 0, 2),
                Padding = new Thickness(10, 8, 12, 8),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinWidth = 310,
                Tag = mode
            };
            System.Windows.Automation.AutomationProperties.SetName(btn, label);
            System.Windows.Automation.AutomationProperties.SetHelpText(btn, description);
            var captured = mode;
            btn.Click += (_, _) => { SelectedMode = captured; Close(); };
            ModeList.Items.Add(btn);
        }

        Deactivated += (_, _) => { if (SelectedMode is null) Close(); };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape: Close(); break;
            case Key.D1: SelectedMode = CaptureMode.Region; Close(); break;
            case Key.D2: SelectedMode = CaptureMode.Window; Close(); break;
            case Key.D3: SelectedMode = CaptureMode.Fullscreen; Close(); break;
            case Key.D4: SelectedMode = CaptureMode.MonitorUnderCursor; Close(); break;
            case Key.D5: SelectedMode = CaptureMode.LastRegion; Close(); break;
            case Key.D6: SelectedMode = CaptureMode.ScrollingWindow; Close(); break;
            case Key.D7: SelectedMode = CaptureMode.SmartElement; Close(); break;
        }
    }

    public static CaptureMode? PickMode()
    {
        var w = new CapturePickerWindow();
        w.ShowDialog();
        return w.SelectedMode;
    }
}
