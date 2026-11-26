using System.Net.Http.Json;
using System.Text.Json;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public interface IAiModelClient
{
    string ProviderId { get; }
    string ProviderName { get; }
    string ModelName { get; }

    Task<ModelInferenceResult> GenerateJsonAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken);
}

public sealed class GeminiAnalysisProvider(
    HttpClient httpClient,
    IConfiguration configuration) : IAiModelClient
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public string ProviderName => "Google Gemini";
    public string ProviderId => "Gemini";
    public string ModelName => configuration["Gemini:Model"] ?? "gemini-2.5-flash";

    public async Task<ModelInferenceResult> GenerateJsonAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken)
    {
        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new AnalysisProviderException(
                "Gemini is configured as the analysis provider, but Gemini:ApiKey is missing.");
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
                temperature = 0.1,
                maxOutputTokens = configuration.GetValue("Gemini:MaxOutputTokens", 8192)
            }
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(ModelName)}:generateContent");
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
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AnalysisProviderException("Gemini inference timed out.", exception);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new AnalysisProviderException(
                    $"Gemini inference failed with HTTP {(int)response.StatusCode} ({response.StatusCode}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonDocument result;
            try
            {
                result = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new AnalysisProviderException("Gemini returned an unreadable provider response.", exception);
            }
            using (result)
            {
                var root = result.RootElement;
                if (!root.TryGetProperty("candidates", out var candidates) ||
                    candidates.ValueKind != JsonValueKind.Array ||
                    candidates.GetArrayLength() == 0 ||
                    !candidates[0].TryGetProperty("content", out var content) ||
                    !content.TryGetProperty("parts", out var parts) ||
                    parts.ValueKind != JsonValueKind.Array)
                {
                    throw new AnalysisProviderException("Gemini returned no structured analysis.");
                }

                var output = string.Join(
                    "",
                    parts.EnumerateArray()
                        .Where(part => part.TryGetProperty("text", out _))
                        .Select(part => part.GetProperty("text").GetString()));
                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new AnalysisProviderException("Gemini returned an empty analysis.");
                }

                return new ModelInferenceResult(
                    output,
                    root.TryGetProperty("modelVersion", out var version) ? version.GetString() : null,
                    GetInt(root, "usageMetadata", "promptTokenCount"),
                    GetInt(root, "usageMetadata", "candidatesTokenCount"));
            }
        }
    }

    private static int? GetInt(JsonElement root, string parentName, string propertyName) =>
        root.TryGetProperty(parentName, out var parent) &&
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
    public string ModelName => configuration["Ollama:Model"] ?? "qwen3:8b";

    public async Task<ModelInferenceResult> GenerateJsonAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken)
    {
        var baseUrl = configuration["Ollama:BaseUrl"] ?? "http://localhost:11434";
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
            options = new { temperature = 0.1 }
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
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AnalysisProviderException("Ollama inference timed out.", exception);
        }
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new AnalysisProviderException(
                    $"Ollama inference failed with HTTP {(int)response.StatusCode} ({response.StatusCode}). " +
                    "Confirm the local Ollama service is running and the configured model is available.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            JsonDocument result;
            try
            {
                result = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new AnalysisProviderException("Ollama returned an unreadable provider response.", exception);
            }
            using (result)
            {
                var root = result.RootElement;
                if (root.TryGetProperty("error", out var error))
                {
                    throw new AnalysisProviderException(
                        $"Ollama returned an error: {error.GetString() ?? "unknown provider error"}.");
                }
                if (!root.TryGetProperty("message", out var message) ||
                    !message.TryGetProperty("content", out var content))
                {
                    throw new AnalysisProviderException("Ollama returned no structured analysis.");
                }

                var output = content.GetString();
                if (string.IsNullOrWhiteSpace(output))
                {
                    throw new AnalysisProviderException("Ollama returned an empty analysis.");
                }

                return new ModelInferenceResult(
                    output,
                    root.TryGetProperty("model", out var model) ? model.GetString() : null,
                    GetInt(root, "prompt_eval_count"),
                    GetInt(root, "eval_count"));
            }
        }
    }

    private static int? GetInt(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var count)
            ? count
            : null;
}
