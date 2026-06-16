namespace Snapture.App.Services;

public sealed class RecordingAudioOptions
{
    public bool IncludeSystemAudio { get; set; } = true;
    public bool IncludeMicrophone { get; set; }
    public bool UseTargetProcessAudio { get; set; }
    public int TargetProcessId { get; set; }

    public RecordingAudioOptions Clone() => new()
    {
        IncludeSystemAudio = IncludeSystemAudio,
        IncludeMicrophone = IncludeMicrophone,
        UseTargetProcessAudio = UseTargetProcessAudio,
        TargetProcessId = TargetProcessId
    };
}
