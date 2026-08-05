namespace Makerverse.AppHost.ApplicationModel;

public enum TranscodePreset {
    Ultrafast,
    Superfast,
    Veryfast,
    Faster,
    Fast,
    Medium,
    Slow,
    Slower,
    VerySlow,
    Placebo
}

/// <summary>
/// Transcode config for livestream-rs
///
/// TranscodeConfig in livestream-rs/crates/livestream-core/src/config.rs
/// </summary>
public class LivestreamTranscodeConfig {
    public ulong Bitrate { get; set; } = 4096;
 
    public TranscodePreset Preset { get; set; } = TranscodePreset.Ultrafast;
    
    public float GopSecs { get; set; } = 2.0f;
    
    public float? Fps { get; set; } = null;
    
    internal string PresetString => Preset.ToString().ToLower();

    public LivestreamTranscodeConfig WithBitrate(ulong bitrate) {
        Bitrate = bitrate;
        return this;
    }

    public LivestreamTranscodeConfig WithPreset(TranscodePreset preset) {
        Preset = preset;
        return this;
    }

    public LivestreamTranscodeConfig WithGopSecs(float gopSecs) {
        GopSecs = gopSecs;
        return this;
    }

    public LivestreamTranscodeConfig WithFps(float? fps) {
        Fps = fps;
        return this;
    }
}