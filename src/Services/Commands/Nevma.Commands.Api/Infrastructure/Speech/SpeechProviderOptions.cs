namespace Nevma.Commands.Api.Infrastructure.Speech;

public sealed class SpeechProviderOptions
{
    public const string SectionName = "SpeechToText";
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "WhisperCpp";
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public long MaxAudioBytes { get; init; } = 25 * 1024 * 1024;
}
