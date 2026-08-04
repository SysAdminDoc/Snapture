using System.IO;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Snapture.App.Editor;

namespace Snapture.App.Views;

/// <summary>
/// One visible editor window that hosts independent editor documents as tabs. The
/// existing EditorWindow remains the stateful document surface; its visual content is
/// reparented here so undo, autosave, annotations, and export paths stay per-tab.
/// </summary>
public partial class EditorTabHostWindow : Window
{
    private static readonly HashSet<string> AcceptedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", SnapFileFormat.Extension
    };

    private static EditorTabHostWindow? _instance;
    private readonly Dictionary<EditorWindow, TabItem> _tabs = new();

    private EditorTabHostWindow()
    {
        InitializeComponent();
        Closed += OnHostClosed;
    }

    public static void Open(EditorWindow editor)
    {
        if (!Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => Open(editor));
            return;
        }

        _instance ??= new EditorTabHostWindow();
        if (!_instance.IsVisible)
            _instance.Show();
        _instance.AddDocument(editor);
        _instance.Activate();
    }

    internal void CloseDocument(EditorWindow editor)
    {
        if (!_tabs.Remove(editor, out var tab)) return;

        editor.DocumentTitleChanged -= OnDocumentTitleChanged;
        editor.DisposeForTabHost();
        EditorTabs.Items.Remove(tab);

        if (_tabs.Count == 0)
            Close();
    }

    private void AddDocument(EditorWindow editor)
    {
        if (_tabs.ContainsKey(editor))
        {
            EditorTabs.SelectedItem = _tabs[editor];
            return;
        }

        var content = editor.DetachContentForTabHost();
        editor.AttachToTabHost(this);

        var tab = new TabItem
        {
            Content = content,
            Tag = editor,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Header = BuildHeader(editor)
        };
        _tabs[editor] = tab;
        editor.DocumentTitleChanged += OnDocumentTitleChanged;
        EditorTabs.Items.Add(tab);
        EditorTabs.SelectedItem = tab;
    }

    private FrameworkElement BuildHeader(EditorWindow editor)
    {
        var label = new TextBlock
        {
            Text = editor.DocumentTitle,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "AppForeground");

        var close = new Button
        {
            Content = "×",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            ToolTip = "Close tab"
        };
        AutomationProperties.SetName(close, $"Close {editor.DocumentTitle}");
        close.Click += (_, e) =>
        {
            e.Handled = true;
            CloseDocument(editor);
        };

        var panel = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(close, Dock.Right);
        panel.Children.Add(close);
        panel.Children.Add(label);
        panel.Tag = label;
        return panel;
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) &&
                    e.Data.GetData(DataFormats.FileDrop) is string[] files &&
                    files.Any(file => AcceptedImageExtensions.Contains(Path.GetExtension(file)))
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) ||
            e.Data.GetData(DataFormats.FileDrop) is not string[] files)
            return;

        foreach (var file in files.Where(file => AcceptedImageExtensions.Contains(Path.GetExtension(file))))
        {
            try
            {
                Open(new EditorWindow(file));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open dropped file:\n{ex.Message}",
                    "Snapture", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        e.Handled = true;
    }

    private void OnDocumentTitleChanged(object? sender, EventArgs e)
    {
        if (sender is not EditorWindow editor || !_tabs.TryGetValue(editor, out var tab)) return;
        if (tab.Header is DockPanel panel && panel.Tag is TextBlock label)
        {
            label.Text = editor.DocumentTitle;
            if (panel.Children.OfType<Button>().FirstOrDefault() is { } close)
                AutomationProperties.SetName(close, $"Close {editor.DocumentTitle}");
        }
    }

    private void OnHostClosed(object? sender, EventArgs e)
    {
        foreach (var editor in _tabs.Keys.ToArray())
        {
            editor.DocumentTitleChanged -= OnDocumentTitleChanged;
            editor.DisposeForTabHost();
        }
        _tabs.Clear();
        EditorTabs.Items.Clear();
        if (ReferenceEquals(_instance, this))
            _instance = null;
    }
}
