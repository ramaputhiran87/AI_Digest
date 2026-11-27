using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using AI.Digest.Api.Data;
using AI.Digest.Api.Models;
using AI.Digest.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);
var configuredDataProtectionKeyPath = builder.Configuration["AI_DATA_PROTECTION_KEYS_PATH"];
var dataProtectionKeyPath = Path.GetFullPath(
    string.IsNullOrWhiteSpace(configuredDataProtectionKeyPath)
        ? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AI.Digest",
            "DataProtectionKeys")
        : configuredDataProtectionKeyPath);
Directory.CreateDirectory(dataProtectionKeyPath);
if (!OperatingSystem.IsWindows())
{
    File.SetUnixFileMode(
        dataProtectionKeyPath,
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
}

var entraConfigured =
    !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:TenantId"]) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"]);
if (entraConfigured)
{
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(
        builder.Configuration.GetSection("AzureAd"));
}
builder.Services.AddAuthorization();
var configuredCorsOrigins = builder.Configuration["Cors:Origins"];
var corsOrigins = string.IsNullOrWhiteSpace(configuredCorsOrigins)
    ? new[] { "http://localhost:4200", "http://127.0.0.1:4200" }
    : configuredCorsOrigins.Split(
        ',',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(corsOrigins)
          .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("analysis", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetAnalysisUserKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 5,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("consent", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetAnalysisUserKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 30,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.AddPolicy("ai-admin", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown-admin",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 10,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        if (context.HttpContext.Request.Path.StartsWithSegments("/api/admin/ai"))
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await JsonSerializer.SerializeAsync(
                context.HttpContext.Response.Body,
                new { error = "Too many administrator requests. Please wait and try again." },
                cancellationToken: cancellationToken);
            return;
        }

        if (context.HttpContext.Request.Path.StartsWithSegments("/api/consent/cloud-ai"))
        {
            context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await JsonSerializer.SerializeAsync(
                context.HttpContext.Response.Body,
                new { error = "Too many consent requests. Please wait and try again." },
                cancellationToken: cancellationToken);
            return;
        }

        await JsonSerializer.SerializeAsync(
            context.HttpContext.Response.Body,
            new
            {
                success = false,
                provider = "",
                model = "",
                analysis = (object?)null,
                summary = "",
                keyFindings = Array.Empty<object>(),
                recommendations = Array.Empty<object>(),
                warnings = Array.Empty<string>(),
                processingTime = 0,
                error = "Too many analysis requests. Please wait and try again."
            },
            cancellationToken: cancellationToken);
    };
});

builder.Services.AddDbContext<DigestDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DigestDb")));
builder.Services.AddSingleton<AgentServices>();
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyPath))
    .SetApplicationName("AI.Digest.Api");
builder.Services.AddSingleton<AiAdminSettingsStore>();
builder.Services.AddSingleton<CloudAiConsentStore>();
builder.Services.AddSingleton<CloudAiConsentService>();
builder.Services.AddSingleton<AiProviderConfiguration>();
builder.Services.AddSingleton<AiProviderHealthTracker>();
builder.Services.AddSingleton<AiInferenceConcurrencyGate>();
builder.Services.AddSingleton<AnalysisProgressHub>();
builder.Services.AddHostedService<AiProviderStartupHealthCheck>();
builder.Services.AddSingleton<AiDailyUsageLimiter>();
builder.Services.AddMemoryCache(options => options.SizeLimit = 1000);
builder.Services.AddSingleton<AnalysisResultCache>();
builder.Services.AddScoped<DeterministicAnalysisProvider>();
builder.Services.AddHttpClient<GeminiAnalysisProvider>(client =>
    client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddHttpClient<GroqAnalysisProvider>(client =>
    client.Timeout = Timeout.InfiniteTimeSpan);
builder.Services.AddScoped<IAiModelClient>(services =>
    services.GetRequiredService<GeminiAnalysisProvider>());
builder.Services.AddScoped<IAiModelClient>(services =>
    services.GetRequiredService<GroqAnalysisProvider>());
builder.Services.AddScoped<AiInferenceRouter>();
builder.Services.AddScoped<LlmAnalysisProvider>();
builder.Services.AddScoped<IAnalysisProvider, AnalysisProviderRouter>();
builder.Services.AddScoped<DocumentAnalysisService>();
builder.Services.AddHttpClient<PresentationSourceProviders>(client =>
    client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<PresentationPlanningService>();
builder.Services.AddScoped<PresentationEditingService>();
builder.Services.AddScoped<PresentationGenerationService>();
builder.Services.AddSingleton<PresentationHistoryStore>();
builder.Services.AddScoped<PresentationStudioService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseCors();
if (entraConfigured)
{
    app.UseAuthentication();
}
app.UseRateLimiter();
app.UseAuthorization();
app.MapOpenApi();
app.MapAiAdminEndpoints();
var consentEndpoints = app.MapGroup("/api/consent/cloud-ai")
    .RequireRateLimiting("consent");
if (entraConfigured)
{
    consentEndpoints.RequireAuthorization();
}
else
{
    consentEndpoints.AddEndpointFilter(async (context, next) =>
    {
        var remoteAddress = context.HttpContext.Connection.RemoteIpAddress;
        if (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
            return Results.Unauthorized();
        return await next(context);
    });
}

consentEndpoints.MapGet("/", (HttpContext context, CloudAiConsentService consent) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    try
    {
        return Results.Ok(consent.GetStatus(context));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
});
consentEndpoints.MapPost("/", (
    HttpContext context,
    CloudAiConsentRequest request,
    CloudAiConsentService consent,
    ILogger<Program> logger) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    try
    {
        return Results.Ok(consent.Grant(context, request));
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new { error = "Consent must match the current cloud AI policy." });
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (CloudAiConsentPersistenceException exception)
    {
        logger.LogError(
            "Cloud AI consent could not be persisted. Exception type: {ExceptionType}",
            exception.InnerException?.GetType().Name ?? exception.GetType().Name);
        return Results.Json(
            new
            {
                code = "AI_CONSENT_PERSISTENCE_FAILED",
                error = "Consent could not be stored. Please try again."
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
consentEndpoints.MapDelete("/", (
    HttpContext context,
    CloudAiConsentService consent,
    ILogger<Program> logger) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    try
    {
        return Results.Ok(consent.Withdraw(context));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (CloudAiConsentPersistenceException exception)
    {
        logger.LogError(
            "Cloud AI consent withdrawal could not be persisted. Exception type: {ExceptionType}",
            exception.InnerException?.GetType().Name ?? exception.GetType().Name);
        return Results.Json(
            new
            {
                code = "AI_CONSENT_PERSISTENCE_FAILED",
                error = "Consent could not be withdrawn. Please try again."
            },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});
app.MapGet("/api/ai/status", (
    HttpContext context,
    AiProviderConfiguration configuration,
    IEnumerable<IAiModelClient> clients,
    AiProviderHealthTracker healthTracker) =>
{
    var providers = clients
        .OrderBy(client => configuration.Priority(client.ProviderId))
        .Select(client =>
        {
            var health = healthTracker.GetSnapshot(client.ProviderId);
            var enabled = configuration.IsEnabledFromConfiguration(client.ProviderId);
            var modelAvailable = configuration.HasSupportedModel(client.ProviderId);
            var credentialConfigured = client.IsConfigured;
            var freeAllowed = configuration.IsAllowedInFreeOnlyMode(client.ProviderId);
            var status = !credentialConfigured
                ? "Not configured"
                : !enabled
                    ? "Disabled"
                    : !modelAvailable
                        ? "Unsupported model"
                        : !freeAllowed
                            ? "Blocked by free-only policy"
                            : health.LastSafeErrorCategory is not null
                                ? "Temporarily unavailable"
                                : health.LastSuccessfulAt is not null
                                    ? "Connected"
                                    : "Connection check pending";
            return new
            {
                providerId = client.ProviderId,
                providerName = client.ProviderName,
                model = client.ModelName,
                enabled,
                credentialConfigured,
                inferenceAllowed = configuration.Enabled && enabled && modelAvailable &&
                                   freeAllowed,
                connectionStatus = status,
                circuitOpen = health.CircuitOpenUntil > DateTimeOffset.UtcNow,
                lastSafeErrorCategory = MapAiErrorCategory(health.LastSafeErrorCategory),
                lastSuccessfulAt = health.LastSuccessfulAt
            };
        })
        .ToArray();
    var usable = providers
        .Where(provider => provider.credentialConfigured &&
                           provider.enabled &&
                           provider.inferenceAllowed)
        .ToArray();
    var active = usable.FirstOrDefault(provider =>
        !provider.circuitOpen && provider.lastSafeErrorCategory is null) ??
        usable.FirstOrDefault(provider => !provider.circuitOpen) ??
        usable.FirstOrDefault();
    var fallback = providers.Skip(1).FirstOrDefault();
    var lastSuccessful = providers
        .Where(provider => provider.lastSuccessfulAt is not null)
        .OrderByDescending(provider => provider.lastSuccessfulAt)
        .FirstOrDefault();
    var canTestConnection = context.User.Identity?.IsAuthenticated == true &&
                            context.User.IsInRole("Admin");
    return Results.Ok(new
    {
        configuredProviders = providers
            .Where(provider => provider.credentialConfigured)
            .Select(provider => provider.providerId),
        serviceStatus = active is not null &&
                        !active.circuitOpen &&
                        active.lastSafeErrorCategory is null
            ? "Online"
            : "Offline",
        activeProvider = active?.providerId,
        activeProviderName = active?.providerName,
        activeModel = active?.model,
        credentialConfigured = active is not null,
        connectionStatus = active?.connectionStatus ??
            (providers.Any(provider => provider.credentialConfigured)
                ? "No provider is available under current policy"
                : "AI service credentials are not configured on the server."),
        fallbackProviderStatus = !configuration.FallbackEnabled
            ? "Disabled"
            : fallback?.connectionStatus ?? "Not configured",
        lastSuccessfulRequest = lastSuccessful is null
            ? null
            : new
            {
                provider = lastSuccessful.providerName,
                occurredAt = lastSuccessful.lastSuccessfulAt
            },
        lastSafeErrorCategory = active?.lastSafeErrorCategory,
        fallbackAvailable = configuration.FallbackEnabled && usable.Length > 1,
        canTestConnection,
        requiresExplicitCloudConsent = configuration.CloudConsentRequired,
        providers
    });
});

var analysisEndpoints = app.MapGroup("/api/documents");
if (entraConfigured)
{
    analysisEndpoints.RequireAuthorization();
}
else
{
    analysisEndpoints.AddEndpointFilter(async (context, next) =>
    {
        var remoteAddress = context.HttpContext.Connection.RemoteIpAddress;
        if (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
            return Results.Unauthorized();
        return await next(context);
    });
}

analysisEndpoints.MapGet("/analysis-progress/{id:guid}", async (
    Guid id,
    HttpContext context,
    AnalysisProgressHub progressHub,
    CancellationToken cancellationToken) =>
{
    var subscription = progressHub.Subscribe(id);
    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache, no-transform";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.Headers["X-Accel-Buffering"] = "no";

    async Task WriteEventAsync(AnalysisProgressEvent progress)
    {
        await context.Response.WriteAsync(
            $"data: {JsonSerializer.Serialize(progress)}\n\n",
            cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    foreach (var progress in subscription.History)
        await WriteEventAsync(progress);
    while (!cancellationToken.IsCancellationRequested)
    {
        using var heartbeatCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var updatesReady = subscription.Updates.WaitToReadAsync(cancellationToken).AsTask();
        var heartbeat = Task.Delay(TimeSpan.FromSeconds(15), heartbeatCancellation.Token);
        if (await Task.WhenAny(updatesReady, heartbeat) == heartbeat)
        {
            await context.Response.WriteAsync(": keep-alive\n\n", cancellationToken);
            await context.Response.Body.FlushAsync(cancellationToken);
            continue;
        }

        heartbeatCancellation.Cancel();
        if (!await updatesReady)
            break;
        while (subscription.Updates.TryRead(out var progress))
            await WriteEventAsync(progress);
    }
    return Results.Empty;
}).DisableAntiforgery();

analysisEndpoints.MapPost("/analyse", async (HttpRequest request,
    IServiceScopeFactory scopeFactory,
    AiProviderConfiguration aiConfiguration,
    IEnumerable<IAiModelClient> aiClients,
    CloudAiConsentService consent,
    AiDailyUsageLimiter dailyUsage,
    AnalysisResultCache resultCache,
    AnalysisProgressHub progressHub,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Multipart document upload required." });

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["file"];
    var useCase = form["useCase"].ToString();
    var analysisMode = form["analysisMode"].ToString();
    var consentScope = form["cloudConsentScope"].ToString();
    var consentPolicyVersion = form["cloudConsentPolicyVersion"].ToString();
    var analysisDepth = form["analysisDepth"].ToString();
    var progressId = Guid.TryParse(form["progressId"], out var parsedProgressId)
        ? parsedProgressId
        : (Guid?)null;
    var consentValid = consent.HasValidConsent(
        request.HttpContext,
        consentScope,
        consentPolicyVersion);
    if (RequiresCloudConsent(aiConfiguration, aiClients, analysisMode) && !consentValid)
        return MapAnalysisFailure(new AnalysisProviderException(
            "Cloud analysis requires your explicit consent.",
            failureCode: AnalysisFailureCode.CloudConsentRequired));
    var allowCloudProcessing = !aiConfiguration.CloudConsentRequired || consentValid;
    var userScope = GetAnalysisUserKey(request.HttpContext);

    if (file is null)
        return Results.BadRequest(new { error = "A document or use-case text is required." });
    if (file.Length == 0)
        return Results.BadRequest(new { error = "The selected document is empty." });
    if (file.Length > 20 * 1024 * 1024)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    if (useCase.Length > aiConfiguration.MaxUseCaseCharacters)
        return Results.BadRequest(new { error = "Analysis context exceeds the configured length limit." });
    if (!IsSupportedDocument(file.FileName))
        return Results.BadRequest(new { error = "Unsupported document type." });

    try
    {
        if (progressId is { } id)
            progressHub.Start(id);
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
            return Results.BadRequest(new { error = "The selected document is empty." });
        if (content.Length > aiConfiguration.MaxInputCharacters)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        if (progressId is { } extractionId)
            progressHub.Report(extractionId, new AnalysisProgressEvent(
                "Content extraction",
                Message: $"{content.Length:N0} characters extracted."));

        var result = await resultCache.GetOrCreateAsync(
            userScope,
            Path.GetFileName(file.FileName),
            content,
            useCase,
            analysisMode,
            allowCloudProcessing,
            async (sharedCancellationToken, sharedProgress) =>
            {
                sharedCancellationToken.ThrowIfCancellationRequested();
                if (!dailyUsage.TryConsume(
                    userScope,
                    aiConfiguration.DailyUsageLimit,
                    DateOnly.FromDateTime(DateTime.UtcNow)))
                {
                    throw new AnalysisProviderException(
                        "Daily analysis limit reached.",
                        failureCode: AnalysisFailureCode.DailyLimitReached);
                }

                using var scope = scopeFactory.CreateScope();
                var analysis = scope.ServiceProvider.GetRequiredService<DocumentAnalysisService>();
                return await analysis.AnalyseAsync(
                    Path.GetFileName(file.FileName),
                    content,
                    useCase,
                    sharedCancellationToken,
                    analysisMode,
                    allowCloudProcessing,
                    analysisDepth,
                    sharedProgress);
            },
            analysisDepth,
            cancellationToken,
            progressId is { } analysisProgressId
                ? new InlineProgress<AnalysisProgressEvent>(
                    progress => progressHub.Report(analysisProgressId, progress))
                : null);
        if (progressId is { } completedId)
            progressHub.Report(completedId, new AnalysisProgressEvent(
                "Analysis completed",
                Completed: true,
                Message: "Analysis finished successfully."));
        return Results.Ok(result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        if (progressId is { } id)
            progressHub.Report(id, new AnalysisProgressEvent(
                "Analysis cancelled",
                Cancelled: true,
                Message: "The request was cancelled; provider work already accepted may still consume quota."));
        throw;
    }
    catch (AnalysisProviderException exception)
    {
        if (progressId is { } id)
            progressHub.Report(id, new AnalysisProgressEvent(
                "Analysis failed",
                Failed: true,
                Message: exception.Message));
        return MapAnalysisFailure(exception);
    }
}).RequireRateLimiting("analysis")
  .DisableAntiforgery()
  .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(22 * 1024 * 1024));

analysisEndpoints.MapPost("/analyse-text", async (AnalyseTextRequest request,
    IServiceScopeFactory scopeFactory,
    AiProviderConfiguration aiConfiguration,
    IEnumerable<IAiModelClient> aiClients,
    CloudAiConsentService consent,
    AiDailyUsageLimiter dailyUsage,
    AnalysisResultCache resultCache,
    AnalysisProgressHub progressHub,
    HttpContext httpContext,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest(new { error = "Use-case text is required." });
    if (request.Text.Length > aiConfiguration.MaxInputCharacters)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    if (request.UseCase?.Length > aiConfiguration.MaxUseCaseCharacters)
        return Results.BadRequest(new { error = "Analysis context exceeds the configured length limit." });

    var consentValid = consent.HasValidConsent(
        httpContext,
        request.CloudConsentScope,
        request.CloudConsentPolicyVersion);
    if (RequiresCloudConsent(aiConfiguration, aiClients, request.AnalysisMode) && !consentValid)
        return MapAnalysisFailure(new AnalysisProviderException(
            "Cloud analysis requires your explicit consent.",
            failureCode: AnalysisFailureCode.CloudConsentRequired));
    var allowCloudProcessing = !aiConfiguration.CloudConsentRequired || consentValid;
    var userScope = GetAnalysisUserKey(httpContext);
    var title = string.IsNullOrWhiteSpace(request.Title)
        ? "Use-case.txt"
        : Path.GetFileName(request.Title);
    try
    {
        if (request.ProgressId is { } progressId)
            progressHub.Start(progressId);
        if (request.ProgressId is { } extractionId)
            progressHub.Report(extractionId, new AnalysisProgressEvent(
                "Content extraction",
                Message: $"{request.Text.Length:N0} characters received."));
        var result = await resultCache.GetOrCreateAsync(
            userScope,
            title,
            request.Text,
            request.UseCase,
            request.AnalysisMode,
            allowCloudProcessing,
            async (sharedCancellationToken, sharedProgress) =>
            {
                sharedCancellationToken.ThrowIfCancellationRequested();
                if (!dailyUsage.TryConsume(
                    userScope,
                    aiConfiguration.DailyUsageLimit,
                    DateOnly.FromDateTime(DateTime.UtcNow)))
                {
                    throw new AnalysisProviderException(
                        "Daily analysis limit reached.",
                        failureCode: AnalysisFailureCode.DailyLimitReached);
                }

                using var scope = scopeFactory.CreateScope();
                var analysis = scope.ServiceProvider.GetRequiredService<DocumentAnalysisService>();
                return await analysis.AnalyseAsync(
                    title,
                    request.Text,
                    request.UseCase,
                    sharedCancellationToken,
                    request.AnalysisMode,
                    allowCloudProcessing,
                    request.AnalysisDepth,
                    sharedProgress);
            },
            request.AnalysisDepth,
            cancellationToken,
            request.ProgressId is { } analysisProgressId
                ? new InlineProgress<AnalysisProgressEvent>(
                    progress => progressHub.Report(analysisProgressId, progress))
                : null);
        if (request.ProgressId is { } completedId)
            progressHub.Report(completedId, new AnalysisProgressEvent(
                "Analysis completed",
                Completed: true,
                Message: "Analysis finished successfully."));
        return Results.Ok(result);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        if (request.ProgressId is { } id)
            progressHub.Report(id, new AnalysisProgressEvent(
                "Analysis cancelled",
                Cancelled: true,
                Message: "The request was cancelled; provider work already accepted may still consume quota."));
        throw;
    }
    catch (AnalysisProviderException exception)
    {
        if (request.ProgressId is { } id)
            progressHub.Report(id, new AnalysisProgressEvent(
                "Analysis failed",
                Failed: true,
                Message: exception.Message));
        return MapAnalysisFailure(exception);
    }
}).RequireRateLimiting("analysis")
  .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(2 * 1024 * 1024));

app.MapGet("/api/documents/{id}/source/{line}", (int id, int line) =>
    Results.Ok(new { documentId = id, line, sourceAvailable = true }));

app.MapPost("/api/documents/export/json", (DigestResult result) =>
    Results.Json(result));

var presentationPreviewEndpoint = app.MapPost("/api/presentations/preview", async (
    HttpRequest request,
    HttpContext httpContext,
    PresentationStudioService studio,
    AiProviderConfiguration aiConfiguration,
    IEnumerable<IAiModelClient> aiClients,
    CloudAiConsentService consent,
    AiDailyUsageLimiter dailyUsage,
    CancellationToken cancellationToken) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(aiConfiguration.RequestTimeoutSeconds));
    try
    {
        PresentationRequest presentationRequest;
        IFormFile? file;
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(timeout.Token);
            presentationRequest = new PresentationRequest
            {
                SourceType = form["sourceType"].ToString(),
                SourceReference = form["sourceReference"].ToString(),
                AnalysisContext = form["analysisContext"].ToString(),
                UseCase = form["useCase"].ToString(),
                Title = form["title"].ToString(),
                TargetAudience = form["targetAudience"].ToString(),
                Purpose = form["purpose"].ToString(),
                PresentationMode = form["presentationMode"].ToString(),
                Duration = form["duration"].ToString(),
                PresentationStyle = form["presentationStyle"].ToString(),
                ContentDepth = form["contentDepth"].ToString(),
                Theme = form["theme"].ToString(),
                SlideCount = int.TryParse(form["slideCount"], out var count) ? count : 10,
                Language = form["language"].ToString(),
                AspectRatio = form["aspectRatio"].ToString(),
                UserInstructions = form["userInstructions"].ToString(),
                VisualIntensity = form["visualIntensity"].ToString(),
                IncludeSpeakerNotes = bool.TryParse(form["includeSpeakerNotes"], out var notes) && notes,
                IncludeArchitectureDiagrams = bool.TryParse(form["includeArchitectureDiagrams"], out var diagrams) && diagrams,
                IncludeScreenshots = bool.TryParse(form["includeScreenshots"], out var screenshots) && screenshots,
                IncludeFlowcharts = bool.TryParse(form["includeFlowcharts"], out var flowcharts) && flowcharts,
                IncludeDataFlowDiagrams = bool.TryParse(form["includeDataFlowDiagrams"], out var dataFlowDiagrams) && dataFlowDiagrams,
                IncludeCharts = bool.TryParse(form["includeCharts"], out var charts) && charts,
                IncludeProblemSolution = bool.TryParse(form["includeProblemSolution"], out var problemSolution) && problemSolution,
                IncludeTechnicalDetails = bool.TryParse(form["includeTechnicalDetails"], out var technicalDetails) && technicalDetails,
                IncludeBusinessRecommendations = bool.TryParse(form["includeBusinessRecommendations"], out var businessRecommendations) && businessRecommendations,
                IncludeRoadmap = bool.TryParse(form["includeRoadmap"], out var roadmap) && roadmap,
                CloudConsentScope = form["cloudConsentScope"].ToString(),
                CloudConsentPolicyVersion = form["cloudConsentPolicyVersion"].ToString()
            };
            file = form.Files["file"];
        }
        else
        {
            presentationRequest = await request.ReadFromJsonAsync<PresentationRequest>(timeout.Token)
                ?? throw new AnalysisProviderException(
                    "A presentation request is required.",
                    failureCode: AnalysisFailureCode.InvalidInput);
            file = null;
        }

        var consentValid = consent.HasValidConsent(
            httpContext,
            presentationRequest.CloudConsentScope,
            presentationRequest.CloudConsentPolicyVersion);
        if (RequiresCloudConsent(aiConfiguration, aiClients, null) && !consentValid)
            return MapAnalysisFailure(new AnalysisProviderException(
                "Cloud analysis requires your explicit consent.",
                failureCode: AnalysisFailureCode.CloudConsentRequired));
        presentationRequest.AllowCloudProcessing =
            !aiConfiguration.CloudConsentRequired || consentValid;
        presentationRequest = PresentationStudioService.ValidateRequest(presentationRequest);
        if (!dailyUsage.TryConsume(
            GetAnalysisUserKey(httpContext),
            aiConfiguration.DailyUsageLimit,
            DateOnly.FromDateTime(DateTime.UtcNow)))
        {
            return MapAnalysisFailure(new AnalysisProviderException(
                "Daily analysis limit reached.",
                failureCode: AnalysisFailureCode.DailyLimitReached));
        }

        var result = await studio.PreviewAsync(presentationRequest, file, timeout.Token);
        return Results.Ok(result);
    }
    catch (AnalysisProviderException exception)
    {
        return MapAnalysisFailure(exception);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return MapAnalysisFailure(new AnalysisProviderException(
            "The presentation request exceeded its configured timeout.",
            failureCode: AnalysisFailureCode.RequestTimeout));
    }
}).RequireRateLimiting("analysis")
  .DisableAntiforgery()
  .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(22 * 1024 * 1024));
if (entraConfigured)
{
    presentationPreviewEndpoint.RequireAuthorization();
}
else
{
    presentationPreviewEndpoint.AddEndpointFilter(async (context, next) =>
    {
        var remoteAddress = context.HttpContext.Connection.RemoteIpAddress;
        if (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
            return Results.Unauthorized();
        return await next(context);
    });
}

app.MapPut("/api/presentations/{id:guid}", (Guid id,
    PresentationDocument presentation,
    PresentationStudioService studio) =>
{
    try
    {
        if (presentation.Id != id)
            return Results.BadRequest(new { error = "Presentation ID mismatch." });
        presentation = new PresentationDocument
        {
            Id = presentation.Id,
            Title = presentation.Title,
            Description = presentation.Description,
            SourceType = presentation.SourceType,
            SourceReference = presentation.SourceReference,
            TargetAudience = presentation.TargetAudience,
            Theme = presentation.Theme,
            ThemeRecommendation = presentation.ThemeRecommendation,
            AspectRatio = presentation.AspectRatio,
            TotalSlides = presentation.Slides.Count,
            CreatedAt = presentation.CreatedAt,
            AIProvider = presentation.AIProvider,
            AIModel = presentation.AIModel,
            Status = presentation.Status,
            Slides = presentation.Slides
        };
        studio.Update(presentation);
        return Results.Ok(presentation);
    }
    catch (AnalysisProviderException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).AllowAnonymous();

var presentationEditEndpoint = app.MapPost("/api/presentations/{id:guid}/edit", async (
    Guid id,
    PresentationEditRequest editRequest,
    HttpContext httpContext,
    PresentationEditingService editor,
    AiProviderConfiguration aiConfiguration,
    IEnumerable<IAiModelClient> aiClients,
    CloudAiConsentService consent,
    AiDailyUsageLimiter dailyUsage,
    CancellationToken cancellationToken) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(aiConfiguration.RequestTimeoutSeconds));
    try
    {
        if (editRequest.Presentation.Id != id)
            return Results.BadRequest(new { error = "Presentation ID mismatch." });

        var consentValid = consent.HasValidConsent(
            httpContext,
            editRequest.CloudConsentScope,
            editRequest.CloudConsentPolicyVersion);
        if (RequiresCloudConsent(aiConfiguration, aiClients, null) && !consentValid)
            return MapAnalysisFailure(new AnalysisProviderException(
                "Cloud AI editing requires your explicit consent.",
                failureCode: AnalysisFailureCode.CloudConsentRequired));
        if (!dailyUsage.TryConsume(
                GetAnalysisUserKey(httpContext),
                aiConfiguration.DailyUsageLimit,
                DateOnly.FromDateTime(DateTime.UtcNow)))
        {
            return MapAnalysisFailure(new AnalysisProviderException(
                "Daily AI usage limit reached.",
                failureCode: AnalysisFailureCode.DailyLimitReached));
        }

        var proposal = await editor.ProposeAsync(
            editRequest,
            !aiConfiguration.CloudConsentRequired || consentValid,
            timeout.Token);
        return Results.Ok(proposal);
    }
    catch (AnalysisProviderException exception)
    {
        return MapAnalysisFailure(exception);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return MapAnalysisFailure(new AnalysisProviderException(
            "The AI editing request exceeded its configured timeout.",
            failureCode: AnalysisFailureCode.RequestTimeout));
    }
}).RequireRateLimiting("analysis")
  .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(22 * 1024 * 1024));
if (entraConfigured)
{
    presentationEditEndpoint.RequireAuthorization();
}
else
{
    presentationEditEndpoint.AddEndpointFilter(async (context, next) =>
    {
        var remoteAddress = context.HttpContext.Connection.RemoteIpAddress;
        if (remoteAddress is null || !IPAddress.IsLoopback(remoteAddress))
            return Results.Unauthorized();
        return await next(context);
    });
}

app.MapGet("/api/presentations", (PresentationStudioService studio) =>
    Results.Ok(studio.History)).AllowAnonymous();

app.MapGet("/api/presentations/{id:guid}", (Guid id, PresentationStudioService studio) =>
{
    try { return Results.Ok(studio.Get(id)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
}).AllowAnonymous();

app.MapGet("/api/presentations/{id:guid}/versions", (Guid id, PresentationStudioService studio) =>
{
    try { return Results.Ok(studio.Versions(id)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
}).AllowAnonymous();

app.MapGet("/api/presentations/{id:guid}/versions/{versionId:guid}",
    (Guid id, Guid versionId, PresentationStudioService studio) =>
    {
        try { return Results.Ok(studio.GetVersion(id, versionId)); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }).AllowAnonymous();

app.MapPost("/api/presentations/{id:guid}/versions/{versionId:guid}/restore",
    (Guid id, Guid versionId, PresentationStudioService studio) =>
    {
        try { return Results.Ok(studio.RestoreVersion(id, versionId)); }
        catch (KeyNotFoundException) { return Results.NotFound(); }
    }).AllowAnonymous();

app.MapGet("/api/presentations/{id:guid}/pptx", (Guid id,
    PresentationStudioService studio) =>
{
    try
    {
        var presentation = studio.Get(id);
        var bytes = studio.Pptx(presentation);
        var name = presentation.Title.Replace(" ", "-").Replace("/", "-") + ".pptx";
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.presentationml.presentation", name);
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (AnalysisProviderException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).AllowAnonymous();

app.MapGet("/api/presentations/{id:guid}/pdf", (Guid id,
    PresentationStudioService studio) =>
{
    try
    {
        var presentation = studio.Get(id);
        var bytes = studio.Pdf(presentation);
        var name = presentation.Title.Replace(" ", "-").Replace("/", "-") + ".pdf";
        return Results.File(bytes, "application/pdf", name);
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (AnalysisProviderException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).AllowAnonymous();

app.MapGet("/api/presentations/{id:guid}/html", (Guid id,
    PresentationStudioService studio) =>
{
    try
    {
        var presentation = studio.Get(id);
        var html = PresentationHtmlExporter.Export(presentation);
        return Results.File(
            System.Text.Encoding.UTF8.GetBytes(html),
            "text/html; charset=utf-8",
            PresentationHtmlExporter.SafeFileName(presentation.Title));
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
}).AllowAnonymous();

app.MapDelete("/api/presentations/{id:guid}", (Guid id,
    PresentationStudioService studio) =>
    studio.Delete(id) ? Results.NoContent() : Results.NotFound()).AllowAnonymous();

app.Run();

static string GetAnalysisUserKey(HttpContext context)
{
    var identity = context.User.FindFirst("sub")?.Value ??
                   context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ??
                   context.Connection.RemoteIpAddress?.ToString() ??
                   "unknown-client";
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
}

static bool RequiresCloudConsent(
    AiProviderConfiguration configuration,
    IEnumerable<IAiModelClient> clients,
    string? analysisMode)
{
    var mode = analysisMode?.Trim();
    var supportedMode =
        string.IsNullOrEmpty(mode) ||
        mode.Equals("Configured", StringComparison.OrdinalIgnoreCase) ||
        mode.Equals("Gemini", StringComparison.OrdinalIgnoreCase) ||
        mode.Equals("Groq", StringComparison.OrdinalIgnoreCase) ||
        mode.Equals("Deterministic", StringComparison.OrdinalIgnoreCase);
    if (!configuration.CloudConsentRequired ||
        !configuration.Enabled ||
        !supportedMode ||
        mode?.Equals("Deterministic", StringComparison.OrdinalIgnoreCase) == true)
    {
        return false;
    }

    var requestedProvider = mode?.Equals("Gemini", StringComparison.OrdinalIgnoreCase) == true
        ? "Gemini"
        : mode?.Equals("Groq", StringComparison.OrdinalIgnoreCase) == true
            ? "Groq"
            : null;
    return clients.Any(client =>
        (requestedProvider is null ||
         client.ProviderId.Equals(requestedProvider, StringComparison.OrdinalIgnoreCase)) &&
        configuration.IsConfigured(client.ProviderId, client) &&
        configuration.IsAllowedInFreeOnlyMode(client.ProviderId));
}

static string? MapAiErrorCategory(string? failureCode) => failureCode switch
{
    nameof(AnalysisFailureCode.NotConfigured) => "AI_CONFIGURATION_MISSING",
    nameof(AnalysisFailureCode.ProviderAuthentication) => "AI_AUTHENTICATION_FAILED",
    nameof(AnalysisFailureCode.ModelUnavailable) => "AI_MODEL_UNAVAILABLE",
    nameof(AnalysisFailureCode.RateLimitExceeded) or
        nameof(AnalysisFailureCode.DailyLimitReached) => "AI_RATE_LIMIT_EXCEEDED",
    nameof(AnalysisFailureCode.RequestTimeout) => "AI_TIMEOUT",
    nameof(AnalysisFailureCode.InvalidResponse) => "AI_INVALID_RESPONSE",
    nameof(AnalysisFailureCode.ProviderUnavailable) => "AI_PROVIDER_UNAVAILABLE",
    nameof(AnalysisFailureCode.Disabled) or
        nameof(AnalysisFailureCode.NoFreeProvider) => "AI_PROVIDER_UNAVAILABLE",
    nameof(AnalysisFailureCode.BudgetExceeded) => "AI_RATE_LIMIT_EXCEEDED",
    _ => null
};

static bool IsSupportedDocument(string fileName) =>
    Path.GetExtension(fileName).ToLowerInvariant() is
        ".md" or ".markdown" or ".txt" or ".html" or ".htm" or ".json" or ".xml";

static IResult MapAnalysisFailure(AnalysisProviderException exception)
{
    var (statusCode, code, message) = exception.FailureCode switch
    {
        AnalysisFailureCode.DailyLimitReached =>
            (StatusCodes.Status429TooManyRequests, "AI_RATE_LIMIT_EXCEEDED", "Daily AI usage limit reached. Please try again later."),
        AnalysisFailureCode.CloudConsentRequired =>
            (StatusCodes.Status403Forbidden, "AI_CLOUD_CONSENT_REQUIRED", "Cloud analysis requires your explicit consent."),
        AnalysisFailureCode.RequestTimeout =>
            (StatusCodes.Status504GatewayTimeout, "AI_TIMEOUT", "Analysis request timed out. Please try again."),
        AnalysisFailureCode.InvalidInput =>
            (StatusCodes.Status400BadRequest, "AI_INVALID_INPUT", "The analysis request is invalid."),
        AnalysisFailureCode.NoFreeProvider =>
            (StatusCodes.Status503ServiceUnavailable, "AI_PROVIDER_UNAVAILABLE", "AI service is temporarily unavailable."),
        AnalysisFailureCode.NotConfigured =>
            (StatusCodes.Status503ServiceUnavailable, "AI_CONFIGURATION_MISSING", "AI service credentials are not configured on the server."),
        AnalysisFailureCode.ProviderAuthentication =>
            (StatusCodes.Status502BadGateway, "AI_AUTHENTICATION_FAILED", "AI provider authentication failed. Contact the administrator."),
        AnalysisFailureCode.ModelUnavailable =>
            (StatusCodes.Status503ServiceUnavailable, "AI_MODEL_UNAVAILABLE", "AI service is temporarily unavailable."),
        AnalysisFailureCode.RateLimitExceeded =>
            (StatusCodes.Status429TooManyRequests, "AI_RATE_LIMIT_EXCEEDED", "AI usage limit reached. Please try again later."),
        AnalysisFailureCode.InvalidResponse =>
            (StatusCodes.Status502BadGateway, "AI_PROVIDER_UNAVAILABLE", "AI service is temporarily unavailable."),
        AnalysisFailureCode.BudgetExceeded =>
            (StatusCodes.Status429TooManyRequests, "AI_REQUEST_BUDGET_EXCEEDED", "The AI request budget was reached. Please try again later."),
        AnalysisFailureCode.Disabled =>
            (StatusCodes.Status503ServiceUnavailable, "AI_PROVIDER_UNAVAILABLE", "AI service is temporarily unavailable."),
        _ =>
            (StatusCodes.Status503ServiceUnavailable, "AI_PROVIDER_UNAVAILABLE", "AI analysis is temporarily unavailable. Please try again.")
    };

    return Results.Json(new
    {
        success = false,
        code,
        error = message,
        provider = "",
        model = "",
        analysis = (object?)null,
        summary = "",
        keyFindings = Array.Empty<object>(),
        recommendations = Array.Empty<object>(),
        warnings = Array.Empty<string>(),
        processingTime = 0
    }, statusCode: statusCode);
}

public partial class Program { }
