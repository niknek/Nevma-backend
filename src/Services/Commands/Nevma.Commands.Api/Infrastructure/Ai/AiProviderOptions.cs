namespace Nevma.Commands.Api.Infrastructure.Ai;

public sealed class AiProviderOptions
{
    public const string SectionName = "CommandAI";
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "Ollama";
    public string? Endpoint { get; init; }
    public string? ApiKey { get; init; }
    public string Model { get; init; } = "qwen3:1.7b";
    public string TranscriptionEndpoint { get; init; } = "https://api.openai.com/v1/audio/transcriptions";
    public string TranscriptionModel { get; init; } = "gpt-4o-transcribe";
}
