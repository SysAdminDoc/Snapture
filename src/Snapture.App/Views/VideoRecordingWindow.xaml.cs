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
    private bool _updatingAudioChecks;
    private bool _updatingZoomToggle;

    public enum Mode { ForegroundWindow, Monitor }

    public VideoRecordingWindow(VideoRecorder recorder, Mode mode, nint targetHandle,
        int fps = 30, int bitrateMbps = 8, int outputWidth = 0, int outputHeight = 0)
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
                    _recorder.StartWindow(targetHandle, _tempPath, fps, bitrateMbps, outputWidth, outputHeight);
                    break;
                case Mode.Monitor:
                    _recorder.StartMonitor(targetHandle, _tempPath, fps, bitrateMbps, outputWidth, outputHeight);
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

        _recorder.SourceClosed += OnSourceClosed;
        if (!SystemParameters.ClientAreaAnimation)
            Loaded += (_, _) => RecDotBlink.Storyboard.Stop(RecDot);
        SyncAudioControls();
        SyncZoomControls();
        UpdateFormatText();
        UpdateProgress();
    }

    private void OnSourceClosed()
    {
        Dispatcher.BeginInvoke(() =>
        {
            StatusLabel.Text = "SOURCE LOST";
            StatusLabel.Foreground = (System.Windows.Media.Brush)FindResource("AppWarning");
            UpdateProgress();
        });
    }

    private void UpdateProgress()
    {
        var ts = _recorder.Elapsed;
        string status = _recorder.IsSourceClosed ? "SOURCE LOST"
            : _recorder.IsPaused ? "PAUSED"
            : "REC";
        string zoom = _recorder.IsZoomSuggestionsEnabled
            ? $"{_recorder.ZoomSuggestionClickCount} zoom"
            : "zoom off";
        ProgressText.Text = $"{_recorder.FrameCount}f - {_recorder.SkippedCleanFrameCount} skipped - {ts:mm\\:ss\\.ff} - {_recorder.SelectedCodecName} - {zoom}";
        StatusLabel.Text = status;
        PauseButton.Content = _recorder.IsPaused ? "Resume" : "Pause";
        PauseButton.IsEnabled = !_recorder.IsSourceClosed;
        UpdateAudioMeters();
    }

    private void SyncAudioControls()
    {
        _updatingAudioChecks = true;
        try
        {
            SystemAudioCheckBox.IsEnabled = _recorder.HasAudioStream;
            MicrophoneCheckBox.IsEnabled = _recorder.HasAudioStream;
            AppAudioOnlyCheckBox.IsEnabled = _recorder.CanUseAppAudio;
            SystemAudioCheckBox.IsChecked = _recorder.IsSystemAudioEnabled;
            MicrophoneCheckBox.IsChecked = _recorder.IsMicrophoneEnabled;
            AppAudioOnlyCheckBox.IsChecked = _recorder.IsAppAudioOnly;
        }
        finally
        {
            _updatingAudioChecks = false;
        }
    }

    private void SyncZoomControls()
    {
        _updatingZoomToggle = true;
        try
        {
            ZoomSuggestionsToggle.IsChecked = _recorder.IsZoomSuggestionsEnabled;
        }
        finally
        {
            _updatingZoomToggle = false;
        }
    }

    private void UpdateAudioMeters()
    {
        double system = Math.Round(Math.Clamp(_recorder.SystemAudioLevel, 0f, 1f) * 100);
        double mic = Math.Round(Math.Clamp(_recorder.MicrophoneLevel, 0f, 1f) * 100);
        SystemAudioMeter.Value = system;
        MicrophoneMeter.Value = mic;
        SystemAudioLevelText.Text = $"{system:0}%";
        MicrophoneLevelText.Text = $"{mic:0}%";
    }

    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        if (_recorder.IsPaused)
            _recorder.Resume();
        else
            _recorder.Pause();
        UpdateProgress();
    }

    private void OnSystemAudioToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingAudioChecks) return;

        bool requested = SystemAudioCheckBox.IsChecked == true;
        bool applied = _recorder.SetSystemAudioEnabled(requested);
        if (requested && !applied)
        {
            _updatingAudioChecks = true;
            SystemAudioCheckBox.IsChecked = false;
            _updatingAudioChecks = false;
        }

        UpdateFormatText();
        UpdateProgress();
    }

    private void OnAppAudioOnlyToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingAudioChecks) return;

        bool requested = AppAudioOnlyCheckBox.IsChecked == true;
        bool applied = _recorder.SetAppAudioOnly(requested);
        if (!applied)
        {
            _updatingAudioChecks = true;
            AppAudioOnlyCheckBox.IsChecked = _recorder.IsAppAudioOnly;
            _updatingAudioChecks = false;
        }

        UpdateFormatText();
        UpdateProgress();
    }

    private void OnMicrophoneToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingAudioChecks) return;

        bool requested = MicrophoneCheckBox.IsChecked == true;
        bool applied = _recorder.SetMicrophoneEnabled(requested);
        if (requested && !applied)
        {
            _updatingAudioChecks = true;
            MicrophoneCheckBox.IsChecked = false;
            _updatingAudioChecks = false;
        }

        UpdateFormatText();
        UpdateProgress();
    }

    private void OnZoomSuggestionsToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingZoomToggle) return;

        _recorder.SetZoomSuggestionsEnabled(ZoomSuggestionsToggle.IsChecked == true);
        UpdateFormatText();
        UpdateProgress();
    }

    private void UpdateFormatText()
    {
        FormatText.Text = $"Recording {_recorder.OutputResolutionDescription} to {_recorder.ContainerDescription} ({_recorder.SelectedCodecDescription}); {_recorder.DirtyRegionDescription}; {_recorder.AudioDescription}; {_recorder.ZoomSuggestionsDescription}; {_recorder.AutoTightenDescription}. Frames stream to disk.";
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
                    _ = _recorder.ExportZoomSuggestions(dlg.FileName);
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
        _recorder.SourceClosed -= OnSourceClosed;
        _recorder.Dispose();
        base.OnClosed(e);
    }
}
