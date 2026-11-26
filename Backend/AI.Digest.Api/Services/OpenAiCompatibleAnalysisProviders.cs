using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public abstract class OpenAiCompatibleAnalysisProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    AiProviderConfiguration aiConfiguration,
    string providerId,
    string providerName) : IAiModelClient, IAiProviderConnectionTester
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public string ProviderId => providerId;
    public string ProviderName => providerName;
    public string ModelName => aiConfiguration.Model(providerId);
    public bool IsConfigured => aiConfiguration.HasApiKey(providerId);

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        var apiKey = aiConfiguration.ResolveApiKey(providerId).ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AnalysisProviderException(
                $"{providerName} API credentials are not configured.",
                failureCode: AnalysisFailureCode.NotConfigured,
                providerId: providerId);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get, new Uri(GetValidatedBaseUrl(), "models"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AnalysisProviderException(
                $"{providerName} could not be reached.",
                exception,
                retryable: true,
                providerId: providerId);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw AiProviderConfiguration.FromHttpResponse(providerId, response);

            try
            {
                using var result = await JsonDocument.ParseAsync(
                    await response.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);
                var modelAvailable =
                    result.RootElement.ValueKind == JsonValueKind.Object &&
                    result.RootElement.TryGetProperty("data", out var models) &&
                    models.ValueKind == JsonValueKind.Array &&
                    models.EnumerateArray().Any(model =>
                        model.ValueKind == JsonValueKind.Object &&
                        model.TryGetProperty("id", out var id) &&
                        id.ValueKind == JsonValueKind.String &&
                        id.GetString()?.Equals(ModelName, StringComparison.Ordinal) == true);
                if (!modelAvailable)
                {
                    throw new AnalysisProviderException(
                        $"{providerName} does not expose the configured model to this API key.",
                        providerId: providerId,
                        failureCode: AnalysisFailureCode.ModelUnavailable,
                        statusCode: HttpStatusCode.NotFound);
                }
            }
            catch (JsonException exception)
            {
                throw new AnalysisProviderException(
                    $"{providerName} returned an unreadable model list.",
                    exception,
                    retryable: true,
                    providerId: providerId,
                    failureCode: AnalysisFailureCode.InvalidResponse);
            }
        }
    }

    public async Task<ModelInferenceResult> GenerateJsonAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken)
    {
        var apiKey = aiConfiguration.ResolveApiKey(providerId).ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AnalysisProviderException(
                $"{providerName} API credentials are not configured.",
                failureCode: AnalysisFailureCode.NotConfigured);
        }

        var endpoint = GetValidatedBaseUrl();

        var requestBody = new
        {
            model = ModelName,
            messages = new[]
            {
                new { role = "system", content = systemInstruction },
                new { role = "user", content = prompt }
            },
            response_format = new { type = "json_object" },
            temperature = AiProviderConfiguration.ReadTemperature(configuration),
            max_tokens = AiProviderConfiguration.ReadMaxOutputTokens(configuration)
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(endpoint, "chat/completions"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(requestBody, options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AnalysisProviderException(
                $"{providerName} could not be reached.",
                exception,
                retryable: true,
                providerId: providerId);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AnalysisProviderException(
                $"{providerName} inference timed out.",
                exception,
                retryable: true,
                providerId: providerId);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw AiProviderConfiguration.FromHttpResponse(providerId, response);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonDocument result;
            try
            {
                result = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new AnalysisProviderException(
                    $"{providerName} returned an unreadable provider response.",
                    exception,
                    retryable: true,
                    providerId: providerId,
                    failureCode: AnalysisFailureCode.InvalidResponse);
            }

            using (result)
            {
                var root = result.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new AnalysisProviderException(
                        $"{providerName} returned an invalid provider response.",
                        retryable: true,
                        providerId: providerId,
                        failureCode: AnalysisFailureCode.InvalidResponse);
                }

                var output = root.TryGetProperty("choices", out var choices) &&
                             choices.ValueKind == JsonValueKind.Array &&
                             choices.GetArrayLength() > 0 &&
                             choices[0].ValueKind == JsonValueKind.Object &&
                             choices[0].TryGetProperty("message", out var message) &&
                             message.ValueKind == JsonValueKind.Object &&
                             message.TryGetProperty("content", out var content)
                    ? GetMessageContent(content)
                    : null;

                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new AnalysisProviderException(
                        $"{providerName} returned no structured analysis.",
                        retryable: true,
                        providerId: providerId,
                        failureCode: AnalysisFailureCode.InvalidResponse);
                }

                return new ModelInferenceResult(
                    output,
                    root.TryGetProperty("model", out var model) &&
                    model.ValueKind == JsonValueKind.String
                        ? model.GetString()
                        : null,
                    GetInt(root, "usage", "prompt_tokens"),
                    GetInt(root, "usage", "completion_tokens"));
            }
        }
    }

    private static string? GetMessageContent(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();

        if (content.ValueKind != JsonValueKind.Array)
            return null;

        return string.Concat(content.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object &&
                           item.TryGetProperty("text", out var text) &&
                           text.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("text").GetString()));
    }

    private Uri GetValidatedBaseUrl()
    {
        var baseUrl = aiConfiguration.BaseUrl(providerId).TrimEnd('/') + "/";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !endpoint.Host.Equals("api.groq.com", StringComparison.OrdinalIgnoreCase) ||
            endpoint.AbsolutePath.TrimEnd('/') != "/openai/v1")
        {
            throw new AnalysisProviderException(
                "Groq base URL must use the official Groq API endpoint.",
                failureCode: AnalysisFailureCode.InvalidInput,
                providerId: providerId);
        }

        return endpoint;
    }

    private static int? GetInt(JsonElement root, string parentName, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(parentName, out var parent) &&
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt32(out var count)
            ? count
            : null;
}

public sealed class GroqAnalysisProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    AiProviderConfiguration aiConfiguration)
    : OpenAiCompatibleAnalysisProvider(
        httpClient,
        configuration,
        aiConfiguration,
        "Groq",
        "Groq Cloud");
