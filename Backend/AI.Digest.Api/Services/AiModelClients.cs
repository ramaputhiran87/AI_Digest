using System.Net.Http.Json;
using System.Text.Json;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public interface IAiModelClient
{
    string ProviderId { get; }
    string ProviderName { get; }
    string ModelName { get; }
    bool IsConfigured => true;

    Task<ModelInferenceResult> GenerateJsonAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken);
}

public interface IAiProviderConnectionTester
{
    Task TestConnectionAsync(CancellationToken cancellationToken);
}

public sealed class GeminiAnalysisProvider(
    HttpClient httpClient,
    IConfiguration configuration,
    AiProviderConfiguration aiConfiguration) : IAiModelClient, IAiProviderConnectionTester
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public string ProviderName => "Google Gemini";
    public string ProviderId => "Gemini";
    public string ModelName => aiConfiguration.Model(ProviderId);
    public bool IsConfigured => aiConfiguration.HasApiKey(ProviderId);

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        var apiKey = aiConfiguration.ResolveApiKey(ProviderId).ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AnalysisProviderException(
                "Gemini API credentials are not configured.",
                failureCode: AnalysisFailureCode.NotConfigured);

        var baseUrl = GetValidatedBaseUrl();
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(baseUrl, $"models/{Uri.EscapeDataString(ModelName)}"));
        request.Headers.Add("x-goog-api-key", apiKey);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AnalysisProviderException(
                "Gemini could not be reached.",
                exception,
                retryable: true,
                providerId: ProviderId);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw AiProviderConfiguration.FromHttpResponse(ProviderId, response);
        }
    }

    public async Task<ModelInferenceResult> GenerateJsonAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken)
    {
        var apiKey = aiConfiguration.ResolveApiKey(ProviderId).ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AnalysisProviderException(
                "Gemini API credentials are not configured.",
                failureCode: AnalysisFailureCode.NotConfigured);
        }

        var requestBody = new
        {
            systemInstruction = new { parts = new[] { new { text = systemInstruction } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = AiProviderConfiguration.ReadTemperature(configuration),
                maxOutputTokens = AiProviderConfiguration.ReadMaxOutputTokens(configuration)
            }
        };
        var endpointBase = GetValidatedBaseUrl();
        var requestUri = new Uri(
            endpointBase,
            $"models/{Uri.EscapeDataString(ModelName)}:generateContent");
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(requestBody, options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AnalysisProviderException(
                "Gemini could not be reached. Check the API key, network connection, and provider availability.",
                exception,
                retryable: true,
                providerId: ProviderId);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AnalysisProviderException("Gemini inference timed out.", exception,
                retryable: true, providerId: ProviderId);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw AiProviderConfiguration.FromHttpResponse(ProviderId, response);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonDocument result;
            try
            {
                result = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new AnalysisProviderException("Gemini returned an unreadable provider response.", exception,
                    retryable: true,
                    providerId: ProviderId,
                    failureCode: AnalysisFailureCode.InvalidResponse);
            }
            using (result)
            {
                var root = result.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("candidates", out var candidates) ||
                    candidates.ValueKind != JsonValueKind.Array ||
                    candidates.GetArrayLength() == 0 ||
                    candidates[0].ValueKind != JsonValueKind.Object ||
                    !candidates[0].TryGetProperty("content", out var content) ||
                    content.ValueKind != JsonValueKind.Object ||
                    !content.TryGetProperty("parts", out var parts) ||
                    parts.ValueKind != JsonValueKind.Array)
                {
                    throw new AnalysisProviderException(
                        "Gemini returned no structured analysis.",
                        retryable: true,
                        providerId: ProviderId,
                        failureCode: AnalysisFailureCode.InvalidResponse);
                }

                var output = string.Join(
                    "",
                    parts.EnumerateArray()
                        .Where(part => part.ValueKind == JsonValueKind.Object &&
                                       part.TryGetProperty("text", out var text) &&
                                       text.ValueKind == JsonValueKind.String)
                        .Select(part => part.GetProperty("text").GetString()));
                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new AnalysisProviderException(
                        "Gemini returned an empty analysis.",
                        retryable: true,
                        providerId: ProviderId,
                        failureCode: AnalysisFailureCode.InvalidResponse);
                }

                return new ModelInferenceResult(
                    output,
                    root.TryGetProperty("modelVersion", out var version) &&
                    version.ValueKind == JsonValueKind.String
                        ? version.GetString()
                        : null,
                    GetInt(root, "usageMetadata", "promptTokenCount"),
                    GetInt(root, "usageMetadata", "candidatesTokenCount"));
            }
        }
    }

    private Uri GetValidatedBaseUrl()
    {
        var baseUrl = aiConfiguration.BaseUrl(ProviderId);
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            !endpoint.Host.Equals(
                "generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase) ||
            endpoint.AbsolutePath.TrimEnd('/') != "/v1beta")
        {
            throw new AnalysisProviderException(
                "Gemini base URL must use the official Google Gemini API endpoint.",
                failureCode: AnalysisFailureCode.InvalidInput,
                providerId: ProviderId);
        }

        return new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
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

public sealed class OllamaAnalysisProvider(
    HttpClient httpClient,
    IConfiguration configuration) : IAiModelClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public string ProviderName => "Ollama";
    public string ProviderId => "Ollama";
    public string ModelName =>
        configuration["AI:Providers:Ollama:Model"] ??
        configuration["Ollama:Model"] ??
        "qwen2.5:3b";
    public bool IsConfigured =>
        configuration.GetValue("AI:Providers:Ollama:Enabled", true);

    public async Task<ModelInferenceResult> GenerateJsonAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken)
    {
        var baseUrl = configuration["AI:Providers:Ollama:BaseUrl"] ??
                      configuration["Ollama:BaseUrl"] ??
                      "http://localhost:11434";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https"))
        {
            throw new AnalysisProviderException("Ollama:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        var requestBody = new
        {
            model = ModelName,
            stream = false,
            format = "json",
            think = false,
            messages = new[]
            {
                new { role = "system", content = systemInstruction },
                new { role = "user", content = prompt }
            },
            options = new
            {
                temperature = AiProviderConfiguration.ReadTemperature(configuration),
                num_predict = AiProviderConfiguration.ReadMaxOutputTokens(configuration)
            }
        };
        var requestUri = new Uri(endpoint, "api/chat");
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                requestUri, requestBody, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AnalysisProviderException(
                "Ollama could not be reached. Confirm the local Ollama service is running.",
                exception,
                retryable: true,
                providerId: ProviderId,
                failureCode: AnalysisFailureCode.NotConfigured);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AnalysisProviderException("Ollama inference timed out.", exception,
                retryable: true, providerId: ProviderId);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw AiProviderConfiguration.FromHttpResponse(ProviderId, response);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonDocument result;
            try
            {
                result = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new AnalysisProviderException("Ollama returned an unreadable provider response.", exception,
                    providerId: ProviderId);
            }
            using (result)
            {
                var root = result.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new AnalysisProviderException(
                        "Ollama returned an invalid provider response.",
                        providerId: ProviderId);
                }
                if (root.TryGetProperty("error", out _))
                {
                    throw new AnalysisProviderException(
                        "Ollama returned an error or the configured model is unavailable.",
                        providerId: ProviderId);
                }
                if (!root.TryGetProperty("message", out var message) ||
                    message.ValueKind != JsonValueKind.Object ||
                    !message.TryGetProperty("content", out var content))
                {
                    throw new AnalysisProviderException(
                        "Ollama returned no structured analysis.",
                        providerId: ProviderId);
                }

                var output = content.ValueKind == JsonValueKind.String
                    ? content.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new AnalysisProviderException(
                        "Ollama returned an empty analysis.",
                        providerId: ProviderId);
                }

                return new ModelInferenceResult(
                    output,
                    root.TryGetProperty("model", out var model) &&
                    model.ValueKind == JsonValueKind.String
                        ? model.GetString()
                        : null,
                    GetInt(root, "prompt_eval_count"),
                    GetInt(root, "eval_count"));
            }
        }
    }

    private static int? GetInt(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var count)
            ? count
            : null;
}

public sealed class OpenAiAnalysisProvider(
    HttpClient httpClient,
    IConfiguration configuration) : IAiModelClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public string ProviderName => "OpenAI";
    public string ProviderId => "OpenAI";
    public string ModelName =>
        configuration["AI:Providers:OpenAI:Model"] ??
        configuration["OpenAI:Model"] ??
        "gpt-6.2-astra";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(configuration["OpenAI:ApiKey"]);

    public async Task<ModelInferenceResult> GenerateJsonAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AnalysisProviderException(
                "OpenAI API credentials are not configured.",
                failureCode: AnalysisFailureCode.NotConfigured);
        }

        var requestBody = new
        {
            model = ModelName,
            instructions = systemInstruction,
            input = prompt,
            text = new { format = new { type = "json_object" } },
            max_output_tokens = AiProviderConfiguration.ReadMaxOutputTokens(configuration)
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(requestBody, options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new AnalysisProviderException(
                "OpenAI could not be reached. Check the API key, network connection, and provider availability.",
                exception,
                retryable: true,
                providerId: ProviderId);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AnalysisProviderException("OpenAI inference timed out.", exception,
                retryable: true, providerId: ProviderId);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw AiProviderConfiguration.FromHttpResponse(ProviderId, response);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonDocument result;
            try
            {
                result = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new AnalysisProviderException("OpenAI returned an unreadable provider response.", exception,
                    providerId: ProviderId);
            }

            using (result)
            {
                var root = result.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    throw new AnalysisProviderException(
                        "OpenAI returned an invalid provider response.",
                        providerId: ProviderId);
                }
                var output = root.TryGetProperty("output", out var outputItems) &&
                             outputItems.ValueKind == JsonValueKind.Array
                    ? string.Concat(outputItems.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.Object &&
                                       item.TryGetProperty("type", out var type) &&
                                       type.ValueKind == JsonValueKind.String &&
                                       type.GetString() == "message")
                        .Where(item => item.TryGetProperty("content", out var content) &&
                                       content.ValueKind == JsonValueKind.Array)
                        .SelectMany(item => item.GetProperty("content").EnumerateArray())
                        .Where(content => content.ValueKind == JsonValueKind.Object &&
                                          content.TryGetProperty("type", out var type) &&
                                          type.ValueKind == JsonValueKind.String &&
                                          type.GetString() == "output_text" &&
                                          content.TryGetProperty("text", out var text) &&
                                          text.ValueKind == JsonValueKind.String)
                        .Select(content => content.GetProperty("text").GetString()))
                    : "";

                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new AnalysisProviderException(
                        "OpenAI returned no structured analysis. The response may be incomplete or refused.");
                }

                return new ModelInferenceResult(
                    output,
                    root.TryGetProperty("model", out var model) &&
                    model.ValueKind == JsonValueKind.String
                        ? model.GetString()
                        : null,
                    GetInt(root, "usage", "input_tokens"),
                    GetInt(root, "usage", "output_tokens"));
            }
        }
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
