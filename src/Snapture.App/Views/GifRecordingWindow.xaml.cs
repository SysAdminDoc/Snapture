using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class GifRecordingWindow : Window
{
    private readonly GifRecorder _recorder;
    private readonly DispatcherTimer _ui;

    public enum Mode { ForegroundWindow, VirtualScreen }

    public GifRecordingWindow(GifRecorder recorder, Mode mode, int targetFps = 10)
    {
        InitializeComponent();
        _recorder = recorder;

        // Park near top-right of primary monitor so it's not over the recording area.
        var screen = System.Windows.SystemParameters.WorkArea;
        Left = screen.Right - Width - 24;
        Top  = screen.Top + 24;

        _ui = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _ui.Tick += (_, _) => UpdateProgress();
        _ui.Start();

        if (!SystemParameters.ClientAreaAnimation)
            Loaded += (_, _) => RecDotBlink.Storyboard.Stop(RecDot);

        try
        {
            switch (mode)
            {
                case Mode.ForegroundWindow: _recorder.StartForegroundWindow(targetFps); break;
                case Mode.VirtualScreen:    _recorder.StartVirtualScreen(targetFps); break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start recording:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }

    private void UpdateProgress()
    {
        var ts = _recorder.Elapsed;
        ProgressText.Text = $"{_recorder.FrameCount} frames · {ts:mm\\:ss\\.ff}";
    }

    private async void OnStopClicked(object sender, RoutedEventArgs e)
    {
        _ui.Stop();
        StopButton.IsEnabled = false;
        DiscardButton.IsEnabled = false;
        try
        {
            await _recorder.StopAsync();
            if (_recorder.FrameCount == 0)
            {
                MessageBox.Show("No frames were captured.", "Snapture",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            using var editor = _recorder.CreateFrameEditor();
            var editorWindow = new GifFrameEditorWindow(editor) { Owner = this };
            editorWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not prepare GIF editor:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _recorder.DisposeFrames();
            Close();
        }
    }

    private async void OnDiscardClicked(object sender, RoutedEventArgs e)
    {
        _ui.Stop();
        StopButton.IsEnabled = false;
        DiscardButton.IsEnabled = false;
        try
        {
            await _recorder.StopAsync();
        }
        finally
        {
            _recorder.DisposeFrames();
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _ui.Stop();
        _recorder.Dispose();
        base.OnClosed(e);
    }
}
