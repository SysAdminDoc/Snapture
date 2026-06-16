using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snapture.App.Views;

public partial class CapturePickerWindow : Window
{
    public enum CaptureMode
    {
        Region, Window, Fullscreen, LastRegion, ScrollingWindow, SmartElement
    }

    private static readonly (CaptureMode mode, string label, string glyph, string hotkey)[] Modes =
    {
        (CaptureMode.Region,          "Region",                    "⊞", "1"),
        (CaptureMode.Window,          "Foreground window",         "⊟", "2"),
        (CaptureMode.Fullscreen,      "Fullscreen (all monitors)", "⊠", "3"),
        (CaptureMode.LastRegion,      "Last region",               "↺", "4"),
        (CaptureMode.ScrollingWindow, "Scrolling window",          "⇕", "5"),
        (CaptureMode.SmartElement,    "Smart element",             "◎", "6"),
    };

    public CaptureMode? SelectedMode { get; private set; }

    public CapturePickerWindow()
    {
        InitializeComponent();

        foreach (var (mode, label, glyph, hotkey) in Modes)
        {
            var btn = new Button
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        new TextBlock { Text = glyph, FontSize = 16, Width = 24, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) },
                        new TextBlock { Text = hotkey, Foreground = (Brush)FindResource("AppSubtleForeground"),
                            FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) },
                    }
                },
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(8, 6, 16, 6),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                MinWidth = 260,
                Tag = mode
            };
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
            case Key.D4: SelectedMode = CaptureMode.LastRegion; Close(); break;
            case Key.D5: SelectedMode = CaptureMode.ScrollingWindow; Close(); break;
            case Key.D6: SelectedMode = CaptureMode.SmartElement; Close(); break;
        }
    }

    public static CaptureMode? PickMode()
    {
        var w = new CapturePickerWindow();
        w.ShowDialog();
        return w.SelectedMode;
    }
}
