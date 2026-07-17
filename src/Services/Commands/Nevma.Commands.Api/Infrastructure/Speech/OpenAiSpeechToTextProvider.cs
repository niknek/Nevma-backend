using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nevma.Commands.Api.Application;
using Nevma.Commands.Api.Infrastructure.Ai;
using Nevma.ServiceDefaults.Extensions;

namespace Nevma.Commands.Api.Infrastructure.Speech;

public sealed class OpenAiSpeechToTextProvider(
    HttpClient client,
    IOptions<AiProviderOptions> options,
    ILogger<OpenAiSpeechToTextProvider> logger) : ISpeechToTextProvider
{
    public async Task<SpeechToTextResult> TranscribeAsync(
        Stream audio,
        string contentType,
        string? language,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.ApiKey) ||
            !TryGetSecureEndpoint(settings.TranscriptionEndpoint, out var endpoint))
            return new SpeechToTextResult.Unavailable();

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        using var form = new MultipartFormDataContent();
        var audioContent = new StreamContent(audio);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(audioContent, "file", FileName(contentType));
        form.Add(new StringContent(settings.TranscriptionModel), "model");
        if (!string.IsNullOrWhiteSpace(language) && language is "el" or "en")
            form.Add(new StringContent(language), "language");
        request.Content = form;

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI transcription returned status {StatusCode}.", (int)response.StatusCode);
                return new SpeechToTextResult.Unavailable();
            }
            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var text = document.RootElement.TryGetProperty("text", out var value) ? value.GetString() : null;
            return !string.IsNullOrWhiteSpace(text) && text.Length <= 2_000
                ? new SpeechToTextResult.Transcribed(text.Trim(), null)
                : new SpeechToTextResult.Invalid("The transcription was empty or too long.");
        }
        catch (Exception exception) when (
            exception is JsonException || exception.IsTransientHttpFailure(cancellationToken))
        {
            logger.LogWarning(exception, "OpenAI transcription request failed.");
            return new SpeechToTextResult.Unavailable();
        }
    }

    private static string FileName(string contentType) => contentType.ToLowerInvariant() switch
    {
        "audio/webm" => "voice.webm",
        "audio/wav" or "audio/x-wav" => "voice.wav",
        "audio/ogg" => "voice.ogg",
        "audio/mpeg" => "voice.mp3",
        _ => "voice.m4a"
    };

    private static bool TryGetSecureEndpoint(string? value, out Uri endpoint)
    {
        var parsed = Uri.TryCreate(value, UriKind.Absolute, out var candidate) ? candidate : null;
        endpoint = candidate!;
        return parsed is not null &&
            (parsed.Scheme == Uri.UriSchemeHttps || parsed.IsLoopback && parsed.Scheme == Uri.UriSchemeHttp);
    }
}
