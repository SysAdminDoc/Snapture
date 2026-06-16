using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Snapture.App.Services;

namespace Snapture.App.Views;

public partial class VideoRecordingWindow : Window
{
    private readonly VideoRecorder _recorder;
    private readonly DispatcherTimer _ui;
    private readonly string _tempPath;

    public enum Mode { ForegroundWindow, Monitor }

    public VideoRecordingWindow(VideoRecorder recorder, Mode mode, nint targetHandle, int fps = 30, int bitrateMbps = 8)
    {
        InitializeComponent();
        _recorder = recorder;

        var screen = SystemParameters.WorkArea;
        Left = screen.Right - Width - 24;
        Top  = screen.Top + 24;

        _ui = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _ui.Tick += (_, _) => UpdateProgress();
        _ui.Start();

        _tempPath = Path.Combine(
            Path.GetTempPath(),
            $"Snapture_rec_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");

        try
        {
            switch (mode)
            {
                case Mode.ForegroundWindow:
                    _recorder.StartWindow(targetHandle, _tempPath, fps, bitrateMbps);
                    break;
                case Mode.Monitor:
                    _recorder.StartMonitor(targetHandle, _tempPath, fps, bitrateMbps);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start recording:\n{ex.Message}", "Snapture",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
            return;
        }

        FormatText.Text = $"Recording to MP4 ({_recorder.SelectedCodecDescription}); {_recorder.DirtyRegionDescription}. Frames stream to disk.";
        UpdateProgress();
    }

    private void UpdateProgress()
    {
        var ts = _recorder.Elapsed;
        string status = _recorder.IsPaused ? "PAUSED" : "REC";
        ProgressText.Text = $"{_recorder.FrameCount} frames - {_recorder.SkippedCleanFrameCount} skipped - {ts:mm\\:ss\\.ff} - MP4 {_recorder.SelectedCodecName}";
        StatusLabel.Text = status;
        PauseButton.Content = _recorder.IsPaused ? "Resume" : "Pause";
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsPaused)
            _recorder.Resume();
        else
            _recorder.Pause();
        UpdateProgress();
    }

    private void OnStopClicked(object sender, RoutedEventArgs e)
    {
        _ui.Stop();
        _recorder.Stop();

        var dlg = new SaveFileDialog
        {
            Filter = "MP4 Video (*.mp4)|*.mp4",
            FileName = $"Snapture_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mp4"
        };
        if (dlg.ShowDialog(this) == true)
        {
            try
            {
                if (File.Exists(_tempPath))
                {
                    File.Copy(_tempPath, dlg.FileName, overwrite: true);
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                        "explorer.exe", $"/select,\"{dlg.FileName}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save recording:\n{ex.Message}", "Snapture",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        CleanupTemp();
        Close();
    }

    private void OnDiscardClicked(object sender, RoutedEventArgs e)
    {
        _ui.Stop();
        _recorder.Stop();
        CleanupTemp();
        Close();
    }

    private void CleanupTemp()
    {
        try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _ui.Stop();
        _recorder.Dispose();
        base.OnClosed(e);
    }
}
