using System.Collections.Concurrent;
using System.Net;

namespace AI.Digest.Api.Services;

public sealed class AiProviderConfiguration(
    IConfiguration configuration,
    AiAdminSettingsStore? adminSettings = null)
{
    private static readonly IReadOnlyDictionary<string, ProviderDefaults> Defaults =
        new Dictionary<string, ProviderDefaults>(StringComparer.OrdinalIgnoreCase)
        {
            ["Gemini"] = new(1, "gemini-3.1-flash-lite"),
            ["Groq"] = new(2, "openai/gpt-oss-120b")
        };

    public bool Enabled => ReadBoolean("AI_ENABLED", "AI:Enabled", true);
    public bool FreeOnly => ReadBoolean("AI_FREE_ONLY", "AI:FreeOnly", true);
    public bool FallbackEnabled =>
        ReadBoolean(
            "AI_FALLBACK_ENABLED",
            "AI:FallbackEnabled",
            adminSettings?.GetFallbackEnabled(),
            true);
    public int RequestTimeoutSeconds => Math.Clamp(
        ReadInteger("AI_REQUEST_TIMEOUT_SECONDS", "AI:RequestTimeoutSeconds", 240), 15, 900);
    public int ProviderTimeoutSeconds => Math.Clamp(
        ReadInteger("AI_PROVIDER_TIMEOUT_SECONDS", "AI:ProviderTimeoutSeconds", 90),
        1, RequestTimeoutSeconds);
    public int RetryLimit => Math.Clamp(
        ReadInteger("AI_RETRY_LIMIT", "AI:RetryLimit", 1), 0, 3);
    public int DailyUsageLimit => Math.Clamp(
        ReadInteger("AI_DAILY_USAGE_LIMIT", "AI:DailyUsageLimit", 10), 1, 1000);
    public int MaxInferenceCallsPerRequest => Math.Clamp(
        ReadInteger(
            "AI_MAX_INFERENCE_CALLS_PER_REQUEST",
            "AI:MaxInferenceCallsPerRequest",
            500),
        1,
        2000);
    public bool FreeTierAnalysisMode =>
        ReadBoolean(
            "AI_FREE_TIER_ANALYSIS_MODE",
            "AI:FreeTierAnalysisMode",
            false);
    public int MaxInputCharacters => Math.Clamp(
        ReadInteger("AI_MAX_INPUT_CHARACTERS", "AI:MaxInputCharacters", 500_000),
        1_000, 2_000_000);
    public int MaxUseCaseCharacters => Math.Clamp(
        ReadInteger("AI_MAX_USE_CASE_CHARACTERS", "AI:MaxUseCaseCharacters", 10_000),
        100, 50_000);
    public double Temperature => ReadTemperature(configuration);
    public int MaxOutputTokens => ReadMaxOutputTokens(configuration);
    public int CircuitFailureThreshold => Math.Clamp(
        ReadInteger(
            "AI_CIRCUIT_BREAKER_FAILURE_THRESHOLD", "AI:CircuitBreaker:FailureThreshold", 3),
        1, 20);
    public TimeSpan CircuitOpenDuration => TimeSpan.FromSeconds(Math.Clamp(
        ReadInteger("AI_CIRCUIT_BREAKER_OPEN_SECONDS", "AI:CircuitBreaker:OpenSeconds", 60),
        5, 3600));
    public bool CloudConsentRequired =>
        ReadBoolean("AI_CLOUD_CONSENT_REQUIRED", "AI:CloudConsentRequired", true);
    public IReadOnlyList<string> ProviderOrder
    {
        get
        {
            var configuredOrder = configuration["AI_PROVIDER_ORDER"] ??
                                  configuration["AI:ProviderOrder"];
            IEnumerable<string> providerIds = string.IsNullOrWhiteSpace(configuredOrder)
                ? ["Gemini", "Groq"]
                : configuredOrder.Split(',', StringSplitOptions.RemoveEmptyEntries |
                                                   StringSplitOptions.TrimEntries);
            var supportedOrder = providerIds
                .Where(Defaults.ContainsKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return supportedOrder.Length > 0
                ? supportedOrder
                : ["Gemini", "Groq"];
        }
    }

    public bool IsEnabled(string providerId) =>
        IsEnabledFromConfiguration(providerId) &&
        HasSupportedModel(providerId);

    public bool IsEnabledFromConfiguration(string providerId) =>
        Defaults.ContainsKey(providerId) &&
        ReadBoolean(
            $"{providerId.ToUpperInvariant()}_ENABLED",
            $"AI:Providers:{providerId}:Enabled",
            adminSettings?.GetEnabled(providerId),
            true);

    public bool HasSupportedModel(string providerId) =>
        providerId.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
            ? Model(providerId).Equals("gemini-3.1-flash-lite", StringComparison.Ordinal)
            : providerId.Equals("Groq", StringComparison.OrdinalIgnoreCase) &&
              Model(providerId).Equals("openai/gpt-oss-120b", StringComparison.Ordinal);

    public bool GeminiFreeTierConfirmed =>
        ReadBoolean(
            "GEMINI_FREE_TIER_CONFIRMED",
            "AI:Providers:Gemini:FreeTierConfirmed",
            adminSettings?.GetGeminiFreeTierConfirmed(),
            false);

    public AiApiKeyResolution ResolveApiKey(string providerId) =>
        adminSettings?.ResolveApiKey(providerId, configuration) ??
        ResolveConfigurationApiKey(providerId);

    public bool HasApiKey(string providerId) =>
        AiAdminSettingsStore.IsUsableApiKey(ResolveApiKey(providerId).ApiKey);

    public int Priority(string providerId)
    {
        var index = ProviderOrder
            .Select((id, order) => (id, order))
            .FirstOrDefault(item =>
                item.id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
        return index.id is not null ? index.order : GetDefaults(providerId).Priority;
    }

    public string Model(string providerId) =>
        configuration[$"{providerId.ToUpperInvariant()}_MODEL"] ??
        adminSettings?.GetModel(providerId) ??
        configuration[$"AI:Providers:{providerId}:Model"] ??
        GetDefaults(providerId).Model;

    public string BaseUrl(string providerId)
    {
        var envName = $"{providerId.ToUpperInvariant()}_BASE_URL";
        var defaultUrl = providerId.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
            ? "https://generativelanguage.googleapis.com/v1beta/"
            : "https://api.groq.com/openai/v1/";
        return configuration[envName] ??
               configuration[$"AI:Providers:{providerId}:BaseUrl"] ??
               defaultUrl;
    }

    public bool IsCloud(string providerId) =>
        Defaults.ContainsKey(providerId);

    public bool IsConfigured(string providerId, IAiModelClient client) =>
        Enabled && IsEnabled(providerId) && client.IsConfigured;

    public bool IsAllowedInFreeOnlyMode(string providerId)
    {
        if (!FreeOnly)
            return true;

        var model = Model(providerId);
        return providerId.ToLowerInvariant() switch
        {
            "gemini" =>
                model.Equals("gemini-3.1-flash-lite", StringComparison.OrdinalIgnoreCase) &&
                GeminiFreeTierConfirmed,
            _ => false
        };
    }

    public static AnalysisProviderException FromHttpResponse(
        string providerId,
        HttpResponseMessage response)
    {
        var status = response.StatusCode;
        var retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is null && response.Headers.RetryAfter?.Date is { } retryDate)
            retryAfter = retryDate - DateTimeOffset.UtcNow;

        return new AnalysisProviderException(
            $"{providerId} returned HTTP {(int)status}.",
            retryable: (int)status >= 500,
            retryAfter: retryAfter,
            providerId: providerId,
            failureCode: status switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                    AnalysisFailureCode.ProviderAuthentication,
                HttpStatusCode.TooManyRequests =>
                    AnalysisFailureCode.RateLimitExceeded,
                HttpStatusCode.NotFound =>
                    AnalysisFailureCode.ModelUnavailable,
                _ => AnalysisFailureCode.ProviderUnavailable
            },
            statusCode: status);
    }

    public static double ReadTemperature(IConfiguration configuration)
    {
        var value = configuration.GetValue(
            "AI_TEMPERATURE",
            configuration.GetValue("AI:Temperature", 0.1));
        return double.IsFinite(value) ? Math.Clamp(value, 0, 2) : 0.1;
    }

    public static int ReadMaxOutputTokens(IConfiguration configuration) =>
        Math.Clamp(
            configuration.GetValue(
                "AI_MAX_OUTPUT_TOKENS",
                configuration.GetValue("AI:MaxOutputTokens", 8192)),
            256,
            32768);

    private bool ReadBoolean(string environmentName, string sectionName, bool defaultValue) =>
        ReadBoolean(environmentName, sectionName, null, defaultValue);

    private bool ReadBoolean(
        string environmentName,
        string sectionName,
        bool? storedValue,
        bool defaultValue)
    {
        if (bool.TryParse(configuration[environmentName], out var environmentValue))
            return environmentValue;
        if (storedValue is not null)
            return storedValue.Value;
        return bool.TryParse(configuration[sectionName], out var sectionValue)
            ? sectionValue
            : defaultValue;
    }

    private AiApiKeyResolution ResolveConfigurationApiKey(string providerId)
    {
        var value = configuration[$"{providerId.ToUpperInvariant()}_API_KEY"];
        return AiAdminSettingsStore.IsUsableApiKey(value)
            ? new AiApiKeyResolution(value!.Trim(), "Environment")
            : ResolveHierarchicalApiKey(providerId, value);
    }

    private AiApiKeyResolution ResolveHierarchicalApiKey(string providerId, string? environmentValue)
    {
        var configuredValue = configuration[$"{providerId}:ApiKey"] ??
                              configuration[$"{providerId}__ApiKey"];
        if (AiAdminSettingsStore.IsUsableApiKey(configuredValue))
            return new AiApiKeyResolution(configuredValue!.Trim(), "Configuration");

        return new AiApiKeyResolution(
            null,
            !string.IsNullOrWhiteSpace(environmentValue)
                ? "Invalid environment value"
                : !string.IsNullOrWhiteSpace(configuredValue)
                    ? "Invalid configuration value"
                    : "Missing");
    }

    private int ReadInteger(string environmentName, string sectionName, int defaultValue) =>
        int.TryParse(configuration[environmentName] ?? configuration[sectionName], out var value)
            ? value
            : defaultValue;

    private static ProviderDefaults GetDefaults(string providerId) =>
        Defaults.TryGetValue(providerId, out var value)
            ? value
            : throw new AnalysisProviderException($"Unsupported AI provider '{providerId}'.");

    private sealed record ProviderDefaults(int Priority, string Model);
}

public enum AnalysisFailureCode
{
    ProviderUnavailable,
    ProviderAuthentication,
    ModelUnavailable,
    RateLimitExceeded,
    InvalidResponse,
    BudgetExceeded,
    Disabled,
    NotConfigured,
    NoFreeProvider,
    CloudConsentRequired,
    RequestTimeout,
    DailyLimitReached,
    InvalidInput
}

public sealed class AiProviderHealthTracker
{
    private readonly ConcurrentDictionary<string, ProviderHealth> providers =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsCircuitOpen(
        string providerId,
        int failureThreshold,
        TimeSpan openDuration,
        DateTimeOffset now)
    {
        var state = providers.GetOrAdd(providerId, _ => new ProviderHealth());
        lock (state)
        {
            if (state.OpenUntil is null)
                return false;
            if (state.OpenUntil <= now)
            {
                state.OpenUntil = null;
                state.Failures = 0;
                return false;
            }

            return true;
        }
    }

    public void RecordFailure(
        string providerId,
        int failureThreshold,
        TimeSpan openDuration,
        DateTimeOffset now,
        AnalysisFailureCode? failureCode = null,
        TimeSpan? cooldown = null)
    {
        var state = providers.GetOrAdd(providerId, _ => new ProviderHealth());
        lock (state)
        {
            state.FailedRequests++;
            state.Failures++;
            state.LastFailureCode = failureCode;
            if (failureCode == AnalysisFailureCode.RateLimitExceeded)
            {
                var rateLimitCooldown = cooldown is { } requested && requested > TimeSpan.Zero
                    ? TimeSpan.FromSeconds(Math.Min(requested.TotalSeconds, 3600))
                    : openDuration;
                state.OpenUntil = now.Add(rateLimitCooldown);
            }
            else if (state.Failures >= failureThreshold)
                state.OpenUntil = now.Add(openDuration);
        }
    }

    public void RecordSuccess(string providerId, TimeSpan elapsed)
    {
        var state = providers.GetOrAdd(providerId, _ => new ProviderHealth());
        lock (state)
        {
            state.SuccessfulRequests++;
            state.Failures = 0;
            state.OpenUntil = null;
            state.LastFailureCode = null;
            state.LastSuccessfulAt = DateTimeOffset.UtcNow;
            state.TotalMilliseconds += elapsed.TotalMilliseconds;
        }
    }

    public ProviderHealthSnapshot GetSnapshot(string providerId)
    {
        var state = providers.GetOrAdd(providerId, _ => new ProviderHealth());
        lock (state)
        {
            return new ProviderHealthSnapshot(
                state.SuccessfulRequests,
                state.FailedRequests,
                state.SuccessfulRequests == 0
                    ? null
                    : state.TotalMilliseconds / state.SuccessfulRequests,
                state.OpenUntil,
                state.LastFailureCode?.ToString(),
                state.LastSuccessfulAt);
        }
    }

    private sealed class ProviderHealth
    {
        public int Failures { get; set; }
        public int SuccessfulRequests { get; set; }
        public int FailedRequests { get; set; }
        public double TotalMilliseconds { get; set; }
        public DateTimeOffset? OpenUntil { get; set; }
        public DateTimeOffset? LastSuccessfulAt { get; set; }
        public AnalysisFailureCode? LastFailureCode { get; set; }
    }
}

public sealed record ProviderHealthSnapshot(
    int SuccessfulRequests,
    int FailedRequests,
    double? AverageResponseMilliseconds,
    DateTimeOffset? CircuitOpenUntil,
    string? LastSafeErrorCategory,
    DateTimeOffset? LastSuccessfulAt);

public sealed class AiDailyUsageLimiter
{
    private readonly ConcurrentDictionary<string, UsageWindow> usage = new(StringComparer.Ordinal);

    public bool TryConsume(string userKey, int dailyLimit, DateOnly today)
    {
        var window = usage.GetOrAdd(userKey, _ => new UsageWindow(today));
        lock (window)
        {
            if (window.Date != today)
            {
                window.Date = today;
                window.Count = 0;
            }

            if (window.Count >= dailyLimit)
                return false;

            window.Count++;
            return true;
        }
    }

    private sealed class UsageWindow(DateOnly date)
    {
        public DateOnly Date { get; set; } = date;
        public int Count { get; set; }
    }
}
