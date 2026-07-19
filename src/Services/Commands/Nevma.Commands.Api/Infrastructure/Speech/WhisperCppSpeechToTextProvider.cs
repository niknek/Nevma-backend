using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nevma.Commands.Api.Application;
using Nevma.ServiceDefaults.Extensions;

namespace Nevma.Commands.Api.Infrastructure.Speech;

public sealed class WhisperCppSpeechToTextProvider(
    HttpClient client,
    IOptions<SpeechProviderOptions> options,
    ILogger<WhisperCppSpeechToTextProvider> logger) : ISpeechToTextProvider
{
    public async Task<SpeechToTextResult> TranscribeAsync(
        Stream audio,
        string contentType,
        string? language,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || !TryGetEndpoint(settings.Endpoint, out var endpoint))
            return new SpeechToTextResult.Unavailable();

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        using var form = new MultipartFormDataContent();
        var audioContent = new StreamContent(audio);
        audioContent.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(audioContent, "file", FileNameFor(contentType));
        form.Add(new StringContent("json"), "response_format");
        form.Add(new StringContent("0.0"), "temperature");
        form.Add(new StringContent(NormalizeLanguage(language)), "language");
        form.Add(new StringContent("Nevma calendar appointment task meeting ραντεβού ημερολόγιο υπενθύμιση"), "prompt");
        request.Content = form;

        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("whisper.cpp returned status {StatusCode}.", (int)response.StatusCode);
                return new SpeechToTextResult.Unavailable();
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var transcript = document.RootElement.TryGetProperty("text", out var text) ? text.GetString() : null;
            return !string.IsNullOrWhiteSpace(transcript) && transcript.Length <= 2_000
                ? new SpeechToTextResult.Transcribed(transcript.Trim(), null)
                : new SpeechToTextResult.Invalid("The transcription was empty or too long.");
        }
        catch (Exception exception) when (
            exception is JsonException || exception.IsTransientHttpFailure(cancellationToken))
        {
            logger.LogWarning(exception, "Local whisper.cpp transcription failed.");
            return new SpeechToTextResult.Unavailable();
        }
    }

    private static string NormalizeLanguage(string? language) => language?.StartsWith("el", StringComparison.OrdinalIgnoreCase) == true
        ? "el"
        : language?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "auto";

    private static string FileNameFor(string contentType) => contentType switch
    {
        "audio/webm" => "voice-input.webm",
        "audio/wav" or "audio/x-wav" => "voice-input.wav",
        "audio/mpeg" => "voice-input.mp3",
        _ => "voice-input.m4a"
    };

    private static bool TryGetEndpoint(string? value, out Uri endpoint)
    {
        var parsed = Uri.TryCreate(value, UriKind.Absolute, out var candidate) ? candidate : null;
        endpoint = candidate!;
        return parsed is not null &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }
}
