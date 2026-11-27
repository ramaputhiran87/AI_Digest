using System.Text.Json;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed record RoutedInferenceResult<T>(
    T Value,
    string ProviderId,
    string ProviderName,
    string ModelName,
    ModelInferenceResult Inference,
    AiInferenceTrace Trace);

public sealed record AiInferenceTrace(
    string ProviderId,
    string ProviderName,
    string ModelName,
    string? ModelVersion,
    IReadOnlyList<AiProviderUsage> ProviderUsage);

public sealed class AiInferenceRouter(
    AiProviderConfiguration configuration,
    IEnumerable<IAiModelClient> modelClients,
    AiProviderHealthTracker healthTracker,
    ILogger<AiInferenceRouter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private int requestCount;

    public async Task<RoutedInferenceResult<T>> GenerateJsonAsync<T>(
        string preferredProviderId,
        string systemInstruction,
        string prompt,
        bool allowCloudProcessing,
        CancellationToken cancellationToken,
        Func<T, T>? validate = null)
    {
        if (!configuration.Enabled)
        {
            throw new AnalysisProviderException(
                "AI analysis is disabled by configuration.",
                failureCode: AnalysisFailureCode.Disabled);
        }

        var clients = modelClients.ToDictionary(
            client => client.ProviderId,
            StringComparer.OrdinalIgnoreCase);
        var ordered = configuration.ProviderOrder
            .Where(clients.ContainsKey)
            .Select(providerId => clients[providerId])
            .OrderBy(client => configuration.Priority(client.ProviderId))
            .ToList();
        var credentialsConfigured = ordered
            .Where(client => client.IsConfigured)
            .ToList();
        var supported = credentialsConfigured
            .Where(client => configuration.HasSupportedModel(client.ProviderId))
            .ToList();
        var configured = supported
            .Where(client => configuration.IsEnabledFromConfiguration(client.ProviderId))
            .ToList();
        var freeEligible = configured
            .Where(client => configuration.IsAllowedInFreeOnlyMode(client.ProviderId))
            .ToList();
        var eligible = freeEligible
            .Where(client => !configuration.CloudConsentRequired ||
                             allowCloudProcessing ||
                             !configuration.IsCloud(client.ProviderId))
            .ToList();

        if (!string.IsNullOrWhiteSpace(preferredProviderId) &&
            !preferredProviderId.Equals("Configured", StringComparison.OrdinalIgnoreCase))
        {
            if (!clients.TryGetValue(preferredProviderId, out var preferred))
            {
                throw new AnalysisProviderException(
                    $"Unsupported AI provider '{preferredProviderId}'.",
                    failureCode: AnalysisFailureCode.InvalidInput);
            }

            if (eligible.Remove(preferred))
                eligible.Insert(0, preferred);
        }

        if (!configuration.FallbackEnabled && eligible.Count > 1)
            eligible = eligible.Take(1).ToList();

        if (eligible.Count == 0)
        {
            var noEligibleFailureCode = credentialsConfigured.Count == 0
                ? AnalysisFailureCode.NotConfigured
                : supported.Count == 0
                    ? AnalysisFailureCode.ModelUnavailable
                    : configured.Count == 0
                        ? AnalysisFailureCode.Disabled
                        : freeEligible.Count == 0
                            ? AnalysisFailureCode.NoFreeProvider
                            : AnalysisFailureCode.CloudConsentRequired;
            throw new AnalysisProviderException(
                "No eligible AI provider is configured for this request.",
                failureCode: noEligibleFailureCode);
        }

        var failures = new List<AnalysisProviderException>();
        var requests = new Dictionary<string, (IAiModelClient Client, int Count)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var client in eligible)
        {
            if (healthTracker.IsCircuitOpen(
                client.ProviderId,
                configuration.CircuitFailureThreshold,
                configuration.CircuitOpenDuration,
                DateTimeOffset.UtcNow))
            {
                logger.LogInformation(
                    "Skipping AI provider {ProviderId}; its circuit is open.",
                    client.ProviderId);
                continue;
            }

            for (var attempt = 0; attempt <= configuration.RetryLimit; attempt++)
            {
                using var providerTimeout =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                providerTimeout.CancelAfter(TimeSpan.FromSeconds(
                    configuration.ProviderTimeoutSeconds));
                var timer = System.Diagnostics.Stopwatch.StartNew();
                if (Interlocked.Increment(ref requestCount) >
                    configuration.MaxInferenceCallsPerRequest)
                {
                    throw new AnalysisProviderException(
                        "The analysis inference-call budget was exceeded.",
                        failureCode: AnalysisFailureCode.BudgetExceeded);
                }
                var previous = requests.TryGetValue(client.ProviderId, out var requestInfo)
                    ? requestInfo
                    : (Client: client, Count: 0);
                requests[client.ProviderId] = (client, previous.Count + 1);

                try
                {
                    var inference = await client.GenerateJsonAsync(
                        systemInstruction, prompt, providerTimeout.Token);
                    T value;
                    try
                    {
                        var jsonContent = NormalizeJsonResponse(inference.Content);
                        value = JsonSerializer.Deserialize<T>(jsonContent, JsonOptions)
                                ?? throw new JsonException("The response was JSON null.");
                        value = validate is null ? value : validate(value);
                    }
                    catch (JsonException exception)
                    {
                        throw new AnalysisProviderException(
                            $"{client.ProviderName} returned invalid JSON.",
                            exception,
                            retryable: true,
                            providerId: client.ProviderId,
                            failureCode: AnalysisFailureCode.InvalidResponse);
                    }
                    catch (AnalysisProviderException exception)
                    {
                        throw new AnalysisProviderException(
                            $"{client.ProviderName} returned an invalid analysis response.",
                            exception,
                            retryable: true,
                            providerId: client.ProviderId,
                            failureCode: AnalysisFailureCode.InvalidResponse);
                    }

                    healthTracker.RecordSuccess(client.ProviderId, timer.Elapsed);
                    var usage = requests.Values
                        .Select(request => new AiProviderUsage(
                            request.Client.ProviderName,
                            request.Client.ModelName,
                            request.Count,
                            request.Client.ProviderId.Equals(
                                    client.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                                request.Count == 1
                                    ? inference.InputTokens
                                    : null,
                            request.Client.ProviderId.Equals(
                                    client.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                                request.Count == 1
                                    ? inference.OutputTokens
                                    : null))
                        .ToArray();
                    return new RoutedInferenceResult<T>(
                        value,
                        client.ProviderId,
                        client.ProviderName,
                        client.ModelName,
                        inference,
                        new AiInferenceTrace(
                            client.ProviderId,
                            client.ProviderName,
                            client.ModelName,
                            inference.ModelVersion,
                            usage));
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    var failure = new AnalysisProviderException(
                        $"{client.ProviderId} inference timed out.",
                        retryable: true,
                        providerId: client.ProviderId,
                        failureCode: AnalysisFailureCode.RequestTimeout);
                    failures.Add(failure);
                    RecordFailure(client.ProviderId, failure);
                    logger.LogWarning(
                        "AI provider {ProviderId} exceeded its configured timeout.",
                        client.ProviderId);
                    if (attempt < configuration.RetryLimit)
                        await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
                }
                catch (AnalysisProviderException exception)
                {
                    if (exception.FailureCode == AnalysisFailureCode.InvalidInput)
                        throw;

                    failures.Add(exception);
                    RecordFailure(client.ProviderId, exception);
                    logger.LogWarning(
                        "AI provider {ProviderId} failed with {FailureCode} and HTTP {StatusCode}; retryable={Retryable}.",
                        client.ProviderId,
                        exception.FailureCode,
                        (int?)exception.StatusCode,
                        exception.Retryable);
                    if (exception.Retryable && attempt < configuration.RetryLimit)
                        await Task.Delay(GetRetryDelay(exception.RetryAfter, attempt), cancellationToken);
                    else
                        break;
                }
            }
        }

        if (cancellationToken.IsCancellationRequested)
            cancellationToken.ThrowIfCancellationRequested();

        if (failures.Count > 0 &&
            failures.All(failure => failure.FailureCode == AnalysisFailureCode.RateLimitExceeded))
        {
            throw new AnalysisProviderException(
                "All eligible AI providers have reached their rate or daily quota.",
                failures.LastOrDefault(),
                failureCode: AnalysisFailureCode.DailyLimitReached,
                statusCode: System.Net.HttpStatusCode.TooManyRequests);
        }

        var failureCode = failures.Count == 0
            ? AnalysisFailureCode.ProviderUnavailable
            : failures.Any(failure =>
                failure.FailureCode == AnalysisFailureCode.RequestTimeout)
                ? AnalysisFailureCode.RequestTimeout
                : failures.All(failure =>
                    failure.FailureCode == AnalysisFailureCode.ProviderAuthentication)
                    ? AnalysisFailureCode.ProviderAuthentication
                    : failures.All(failure =>
                        failure.FailureCode == AnalysisFailureCode.ModelUnavailable)
                        ? AnalysisFailureCode.ModelUnavailable
                        : failures.All(failure =>
                            failure.FailureCode == AnalysisFailureCode.InvalidResponse)
                            ? AnalysisFailureCode.InvalidResponse
                            : AnalysisFailureCode.ProviderUnavailable;
        var lastFailure = failures.LastOrDefault();
        throw new AnalysisProviderException(
            "All eligible AI providers failed to complete the inference.",
            lastFailure?.InnerException ?? lastFailure,
            failureCode: failureCode);
    }

    private void RecordFailure(string providerId, AnalysisProviderException exception) =>
        healthTracker.RecordFailure(
            providerId,
            configuration.CircuitFailureThreshold,
            configuration.CircuitOpenDuration,
            DateTimeOffset.UtcNow,
            exception.FailureCode,
            exception.FailureCode == AnalysisFailureCode.RateLimitExceeded
                ? exception.RetryAfter
                : null);

    private TimeSpan GetRetryDelay(TimeSpan? retryAfter, int attempt) =>
        retryAfter is { } delay
            ? TimeSpan.FromSeconds(Math.Clamp(
                delay.TotalSeconds, 0, configuration.RequestTimeoutSeconds))
            : TimeSpan.FromMilliseconds(Math.Min(500 * Math.Pow(2, attempt), 4000));

    private static string NormalizeJsonResponse(string content)
    {
        var trimmed = content.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var newline = trimmed.IndexOf('\n');
            if (newline >= 0)
                trimmed = trimmed[(newline + 1)..].Trim();
            if (trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed[..^3].Trim();
        }

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            return trimmed;

        var objectStart = trimmed.IndexOf('{');
        var arrayStart = trimmed.IndexOf('[');
        var start = objectStart >= 0 && (arrayStart < 0 || objectStart < arrayStart)
            ? objectStart
            : arrayStart;
        if (start < 0)
            return trimmed;

        var objectEnd = trimmed.LastIndexOf('}');
        var arrayEnd = trimmed.LastIndexOf(']');
        var end = Math.Max(objectEnd, arrayEnd);
        return end >= start ? trimmed[start..(end + 1)] : trimmed;
    }
}
