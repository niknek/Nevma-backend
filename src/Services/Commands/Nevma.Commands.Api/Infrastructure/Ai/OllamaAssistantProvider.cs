using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Nevma.Commands.Api.Application;
using Nevma.Contracts.Commands;
using Nevma.ServiceDefaults.Extensions;

namespace Nevma.Commands.Api.Infrastructure.Ai;

public sealed class OllamaAssistantProvider(
    HttpClient client,
    IOptions<AiProviderOptions> options,
    ILogger<OllamaAssistantProvider> logger) : IAiAssistantProvider
{
    public async Task<AssistantProviderResult> RespondAsync(
        AssistantRequest request,
        DateTimeOffset localNow,
        CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled || !TryGetEndpoint(settings.Endpoint, out var endpoint))
            return new AssistantProviderResult.Unavailable(
                "Nevma local AI is not configured. Start Ollama and enable CommandAI.");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                model = settings.Model,
                stream = false,
                think = false,
                keep_alive = "30m",
                format = AssistantProtocol.Schema,
                options = new { temperature = 0.1, num_ctx = 4096, num_predict = 512 },
                messages = new object[]
                {
                    new { role = "system", content = AssistantProtocol.Instructions },
                    new
                    {
                        role = "user",
                        content = JsonSerializer.Serialize(
                            AssistantProtocol.CreateContext(request, localNow),
                            AssistantProtocol.JsonOptions)
                    }
                }
            }, options: AssistantProtocol.JsonOptions)
        };

        try
        {
            using var response = await client.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Ollama assistant returned status {StatusCode}.", (int)response.StatusCode);
                return new AssistantProviderResult.Unavailable("Nevma local AI is temporarily unavailable.");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
            var content = document.RootElement.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var value)
                    ? value.GetString()
                    : null;
            return string.IsNullOrWhiteSpace(content)
                ? new AssistantProviderResult.Unavailable("Nevma local AI returned an empty response.")
                : AssistantProtocol.Parse(content);
        }
        catch (Exception exception) when (
            exception is JsonException || exception.IsTransientHttpFailure(cancellationToken))
        {
            logger.LogWarning(exception, "Ollama assistant request failed.");
            return new AssistantProviderResult.Unavailable("Nevma local AI is temporarily unavailable.");
        }
    }

    private static bool TryGetEndpoint(string? value, out Uri endpoint)
    {
        var parsed = Uri.TryCreate(value, UriKind.Absolute, out var candidate) ? candidate : null;
        endpoint = candidate!;
        return parsed is not null &&
            (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);
    }
}
