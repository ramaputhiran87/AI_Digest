using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;

namespace AI.Digest.Api.Services;

public static class AiAdminApi
{
    public static RouteGroupBuilder MapAiAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/admin/ai")
            .RequireRateLimiting("ai-admin");

        var entraConfigured =
            !string.IsNullOrWhiteSpace(app.Configuration["AzureAd:TenantId"]) &&
            !string.IsNullOrWhiteSpace(app.Configuration["AzureAd:ClientId"]);
        if (entraConfigured)
        {
            group.RequireAuthorization(policy => policy.RequireRole("Admin"));
        }
        else
        {
            group.AddEndpointFilter(async (context, next) =>
            {
                var configuration = context.HttpContext.RequestServices
                    .GetRequiredService<IConfiguration>();
                var authorizationFailure = AiAdminAccess.Validate(
                    context.HttpContext,
                    configuration["AI_ADMIN_TOKEN"]);
                return authorizationFailure ?? await next(context);
            });
        }

        group.MapGet("/configuration", (
            AiProviderConfiguration configuration,
            AiProviderHealthTracker healthTracker) =>
            Results.Ok(CreateConfigurationResponse(configuration, healthTracker)));

        group.MapPut("/configuration", (
            AiAdminSettingsUpdate update,
            AiAdminSettingsStore settingsStore,
            AiProviderConfiguration configuration,
            AiProviderHealthTracker healthTracker) =>
        {
            try
            {
                settingsStore.Save(update);
                return Results.Ok(CreateConfigurationResponse(
                    configuration, healthTracker));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new
                {
                    error = "Only the configured Gemini and Groq models are supported. API keys must not be empty or placeholders."
                });
            }
        });

        group.MapPost("/providers/{providerId}/test", async (
            string providerId,
            IEnumerable<IAiModelClient> clients,
            AiProviderConfiguration configuration,
            AiProviderHealthTracker healthTracker,
            CancellationToken cancellationToken) =>
        {
            if (!providerId.Equals("gemini", StringComparison.OrdinalIgnoreCase) &&
                !providerId.Equals("groq", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound(new { error = "Unsupported AI provider." });

            var client = clients.Single(candidate =>
                candidate.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            if (!configuration.IsEnabledFromConfiguration(client.ProviderId))
                return Results.Conflict(new { error = "Enable this provider before testing its connection." });
            if (!configuration.HasSupportedModel(client.ProviderId))
                return Results.Conflict(new { error = "The backend model setting does not match the supported model." });
            if (!client.IsConfigured)
                return Results.Conflict(new { error = "No valid API key is configured for this provider." });
            if (client is not IAiProviderConnectionTester connectionTester)
                return Results.StatusCode(StatusCodes.Status501NotImplemented);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(configuration.ProviderTimeoutSeconds));
            var timer = Stopwatch.StartNew();
            try
            {
                await connectionTester.TestConnectionAsync(timeout.Token);
                healthTracker.RecordSuccess(client.ProviderId, timer.Elapsed);
                var inferenceBlocked = !configuration.IsAllowedInFreeOnlyMode(client.ProviderId);
                return Results.Ok(new
                {
                    success = true,
                    provider = client.ProviderName,
                    model = client.ModelName,
                    status = inferenceBlocked
                        ? "Connected; inference is blocked by free-only mode."
                        : "Connected.",
                    inferenceBlocked
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                RecordFailure(client.ProviderId, configuration, healthTracker);
                return Results.Json(
                    new
                    {
                        success = false,
                        status = "Timed out.",
                        error = "Provider connection test timed out."
                    },
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (AnalysisProviderException exception)
            {
                RecordFailure(
                    client.ProviderId,
                    configuration,
                    healthTracker,
                    exception.FailureCode,
                    exception.RetryAfter);
                var status = exception.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                        (StatusCodes.Status401Unauthorized, "Credentials rejected."),
                    HttpStatusCode.NotFound =>
                        (StatusCodes.Status404NotFound, "The configured model is unavailable to this API key."),
                    HttpStatusCode.TooManyRequests =>
                        (StatusCodes.Status429TooManyRequests, "The provider rate limit was reached."),
                    _ =>
                        (StatusCodes.Status502BadGateway, "Provider connection failed. Check the key, network, and provider status.")
                };
                return Results.Json(
                    new { success = false, status = status.Item2, error = status.Item2 },
                    statusCode: status.Item1);
            }
        });

        group.MapPost("/providers/{providerId}/test-inference", async (
            string providerId,
            IEnumerable<IAiModelClient> clients,
            AiProviderConfiguration configuration,
            AiProviderHealthTracker healthTracker,
            CancellationToken cancellationToken) =>
        {
            if (!providerId.Equals("gemini", StringComparison.OrdinalIgnoreCase) &&
                !providerId.Equals("groq", StringComparison.OrdinalIgnoreCase))
                return Results.NotFound(new { error = "Unsupported AI provider." });

            var client = clients.Single(candidate =>
                candidate.ProviderId.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            if (!configuration.IsConfigured(client.ProviderId, client))
                return Results.Conflict(new { error = "Enable the provider and configure a valid API key first." });
            if (!configuration.IsAllowedInFreeOnlyMode(client.ProviderId))
                return Results.Conflict(new
                {
                    error = "Inference is blocked by free-only mode for this provider and model."
                });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(configuration.ProviderTimeoutSeconds));
            var timer = Stopwatch.StartNew();
            try
            {
                var result = await client.GenerateJsonAsync(
                    "Return only the JSON object requested by the user.",
                    """Return exactly {"connection":"ok"}.""",
                    timeout.Token);
                using var response = JsonDocument.Parse(result.Content);
                if (response.RootElement.ValueKind != JsonValueKind.Object)
                    throw new AnalysisProviderException(
                        "The provider returned an invalid inference test response.",
                        providerId: client.ProviderId,
                        failureCode: AnalysisFailureCode.InvalidResponse);

                healthTracker.RecordSuccess(client.ProviderId, timer.Elapsed);
                return Results.Ok(new
                {
                    success = true,
                    provider = client.ProviderName,
                    model = client.ModelName,
                    status = "Inference test succeeded.",
                    inputTokens = result.InputTokens,
                    outputTokens = result.OutputTokens
                });
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                RecordFailure(
                    client.ProviderId,
                    configuration,
                    healthTracker,
                    AnalysisFailureCode.RequestTimeout);
                return Results.Json(
                    new
                    {
                        success = false,
                        status = "Timed out.",
                        error = "Provider inference test timed out."
                    },
                    statusCode: StatusCodes.Status504GatewayTimeout);
            }
            catch (AnalysisProviderException exception)
            {
                RecordFailure(
                    client.ProviderId,
                    configuration,
                    healthTracker,
                    exception.FailureCode,
                    exception.RetryAfter);
                var status = exception.FailureCode switch
                {
                    AnalysisFailureCode.ProviderAuthentication =>
                        (StatusCodes.Status401Unauthorized, "Credentials rejected."),
                    AnalysisFailureCode.ModelUnavailable =>
                        (StatusCodes.Status404NotFound, "The configured model is unavailable."),
                    AnalysisFailureCode.RateLimitExceeded =>
                        (StatusCodes.Status429TooManyRequests, "The provider rate limit was reached."),
                    AnalysisFailureCode.InvalidResponse =>
                        (StatusCodes.Status502BadGateway, "The provider returned an invalid response."),
                    _ =>
                        (StatusCodes.Status502BadGateway, "Provider inference test failed.")
                };
                return Results.Json(
                    new { success = false, status = status.Item2, error = status.Item2 },
                    statusCode: status.Item1);
            }
            catch (JsonException)
            {
                RecordFailure(
                    client.ProviderId,
                    configuration,
                    healthTracker,
                    AnalysisFailureCode.InvalidResponse);
                return Results.Json(
                    new
                    {
                        success = false,
                        status = "Invalid response.",
                        error = "The provider returned an invalid response."
                    },
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        return group;
    }

    private static AiAdminConfigurationResponse CreateConfigurationResponse(
        AiProviderConfiguration configuration,
        AiProviderHealthTracker healthTracker)
    {
        var providers = new[] { "Gemini", "Groq" }
            .Select(providerId =>
            {
                var model = configuration.Model(providerId);
                var key = configuration.ResolveApiKey(providerId);
                var enabled = configuration.IsEnabled(providerId);
                var health = healthTracker.GetSnapshot(providerId);
                var blockedByFreeOnly = configuration.FreeOnly &&
                                        !configuration.IsAllowedInFreeOnlyMode(providerId);
                var status = !configuration.HasSupportedModel(providerId)
                    ? "Unsupported model configured"
                    : !configuration.IsEnabledFromConfiguration(providerId)
                        ? "Disabled"
                        : !configuration.HasApiKey(providerId)
                            ? key.Source.StartsWith("Invalid", StringComparison.Ordinal)
                                ? "Invalid or placeholder API key"
                                : "API key required"
                            : blockedByFreeOnly
                                ? "Blocked by free-only mode"
                                : health.SuccessfulRequests > 0
                                    ? "Connected"
                                    : health.FailedRequests > 0
                                        ? "Connection test failed"
                                        : "Configured; connection not tested";

                return new AiAdminProviderStatus(
                    providerId,
                    model,
                    enabled,
                    configuration.HasApiKey(providerId),
                    key.Source,
                    status,
                    blockedByFreeOnly,
                    health.SuccessfulRequests + health.FailedRequests,
                    health.FailedRequests,
                    health.AverageResponseMilliseconds);
            })
            .ToArray();

        return new AiAdminConfigurationResponse(
            configuration.FallbackEnabled,
            configuration.FreeOnly,
            configuration.GeminiFreeTierConfirmed,
            providers);
    }

    private static void RecordFailure(
        string providerId,
        AiProviderConfiguration configuration,
        AiProviderHealthTracker healthTracker,
        AnalysisFailureCode? failureCode = null,
        TimeSpan? cooldown = null) =>
        healthTracker.RecordFailure(
            providerId,
            configuration.CircuitFailureThreshold,
            configuration.CircuitOpenDuration,
            DateTimeOffset.UtcNow,
            failureCode,
            cooldown);
}

public sealed record AiAdminProviderStatus(
    string ProviderId,
    string Model,
    bool Enabled,
    bool ApiKeyConfigured,
    string ApiKeySource,
    string ConnectionStatus,
    bool InferenceBlockedByFreeOnly,
    int RequestCount,
    int FailedRequestCount,
    double? AverageResponseMilliseconds);

public sealed record AiAdminConfigurationResponse(
    bool AutomaticFallback,
    bool FreeOnly,
    bool GeminiFreeTierConfirmed,
    IReadOnlyList<AiAdminProviderStatus> Providers);
