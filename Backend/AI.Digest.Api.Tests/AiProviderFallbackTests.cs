using System.Net;
using System.Text;
using System.Text.Json;
using AI.Digest.Api.Models;
using AI.Digest.Api.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AI.Digest.Api.Tests;

public sealed class AiProviderFallbackTests
{
    [Fact]
    public async Task GroqUsesOfficialEndpointModelAndParsesOpenAiCompatibleResponse()
    {
        var handler = new ProviderHandler(HttpStatusCode.OK, """
            {
              "model": "provider/free-model",
              "choices": [{ "message": { "content": "{\"ok\":true}" } }],
              "usage": { "prompt_tokens": 17, "completion_tokens": 9 }
            }
            """);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GROQ_API_KEY"] = "test-key",
                ["GROQ_MODEL"] = "openai/gpt-oss-120b",
                ["GROQ_BASE_URL"] = "https://api.groq.com/openai/v1"
            })
            .Build();
        var provider = CreateGroqProvider(new HttpClient(handler), configuration);

        var result = await provider.GenerateJsonAsync("system", "prompt", CancellationToken.None);

        Assert.Equal("https://api.groq.com/openai/v1/chat/completions", handler.RequestUri?.ToString());
        Assert.Equal("Bearer test-key", handler.Authorization);
        Assert.Equal("openai/gpt-oss-120b", handler.Model);
        Assert.Equal("{\"ok\":true}", result.Content);
        Assert.Equal(17, result.InputTokens);
        Assert.Equal(9, result.OutputTokens);
    }

    [Fact]
    public async Task GeminiConnectionTestChecksTheConfiguredModelWithoutInference()
    {
        var handler = new ProviderHandler(
            HttpStatusCode.OK,
            """{"name":"models/gemini-3.1-flash-lite"}""");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = "test-gemini-key"
            })
            .Build();
        var provider = new GeminiAnalysisProvider(
            new HttpClient(handler), configuration, new AiProviderConfiguration(configuration));

        await provider.TestConnectionAsync(CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite",
            handler.RequestUri?.ToString());
        Assert.Equal("test-gemini-key", handler.GoogleApiKey);
        Assert.Null(handler.Model);
    }

    [Fact]
    public void GeminiConfigurationSectionIsDetectedWithoutGroqCredentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "existing-gemini-api-key"
            })
            .Build();
        var policy = new AiProviderConfiguration(configuration);
        var gemini = new GeminiAnalysisProvider(
            new HttpClient(), configuration, policy);

        Assert.True(gemini.IsConfigured);
        Assert.True(policy.IsConfigured(gemini.ProviderId, gemini));
        Assert.False(policy.HasApiKey("Groq"));
    }

    [Theory]
    [InlineData("GEMINI_API_KEY", "Gemini")]
    [InlineData("Gemini__ApiKey", "Gemini")]
    [InlineData("Gemini:ApiKey", "Gemini")]
    [InlineData("GROQ_API_KEY", "Groq")]
    [InlineData("Groq__ApiKey", "Groq")]
    [InlineData("Groq:ApiKey", "Groq")]
    public void ProviderCredentialsResolveFromSupportedConfigurationNames(
        string configurationName,
        string providerId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [configurationName] = "test-provider-key"
            })
            .Build();

        var result = new AiProviderConfiguration(configuration).ResolveApiKey(providerId);

        Assert.Equal("test-provider-key", result.ApiKey);
    }

    [Fact]
    public void ProviderHealthRecordsTheMostRecentSuccessfulRequest()
    {
        var healthTracker = new AiProviderHealthTracker();

        healthTracker.RecordSuccess("Gemini", TimeSpan.FromMilliseconds(125));
        var snapshot = healthTracker.GetSnapshot("Gemini");

        Assert.Equal(1, snapshot.SuccessfulRequests);
        Assert.NotNull(snapshot.LastSuccessfulAt);
    }

    [Fact]
    public async Task GroqConnectionTestChecksModelListWithoutInference()
    {
        var handler = new ProviderHandler(
            HttpStatusCode.OK,
            """{"data":[{"id":"openai/gpt-oss-120b"}]}""");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GROQ_API_KEY"] = "test-groq-key"
            })
            .Build();
        var provider = CreateGroqProvider(new HttpClient(handler), configuration);

        await provider.TestConnectionAsync(CancellationToken.None);

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://api.groq.com/openai/v1/models", handler.RequestUri?.ToString());
        Assert.Equal("Bearer test-groq-key", handler.Authorization);
        Assert.Null(handler.Model);
    }

    [Fact]
    public async Task PresentationResolvesConfiguredModeToPrimaryAiProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Analysis:Provider"] = "Configured",
                ["AI:FreeOnly"] = "false",
                ["AI:CloudConsentRequired"] = "false",
                ["AI:RetryLimit"] = "0"
            })
            .Build();
        var providerConfiguration = new AiProviderConfiguration(configuration);
        var client = new StaticAiModelClient(JsonSerializer.Serialize(new
        {
            description = "A source-grounded overview.",
            recommendedTheme = "Neural Intelligence",
            slides = Enumerable.Range(1, 5)
                .Select(slideNumber => new
                {
                    sectionId = $"section-{slideNumber}",
                    slideNumber,
                    title = $"Slide {slideNumber}"
                })
        }));
        var inferenceRouter = new AiInferenceRouter(
            providerConfiguration,
            [client],
            new AiProviderHealthTracker(),
            NullLogger<AiInferenceRouter>.Instance);
        var planner = new PresentationPlanningService(
            configuration, inferenceRouter, providerConfiguration);

        var presentation = await planner.PlanAsync(
            new PresentationRequest { SlideCount = 5, Theme = "Auto" },
            new PresentationSource { Text = "The source describes a documented service." },
            CancellationToken.None);

        Assert.Equal("Google Gemini", presentation.AIProvider);
        Assert.Equal(5, presentation.Slides.Count);
        Assert.Equal("Neural Intelligence", presentation.Theme);
        Assert.Equal("Neural Intelligence", presentation.ThemeRecommendation);
        Assert.Equal("section-1", presentation.Slides[0].SectionId);
    }

    [Fact]
    public async Task ProviderRateLimitPreservesRetryAfterWithoutExposingBody()
    {
        var handler = new ProviderHandler(
            HttpStatusCode.TooManyRequests,
            """{"error":{"message":"private provider diagnostic"}}""",
            TimeSpan.FromSeconds(3));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GROQ_API_KEY"] = "test-key"
            })
            .Build();
        var provider = CreateGroqProvider(new HttpClient(handler), configuration);

        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            provider.GenerateJsonAsync("system", "prompt", CancellationToken.None));

        Assert.False(exception.Retryable);
        Assert.Equal(AnalysisFailureCode.RateLimitExceeded, exception.FailureCode);
        Assert.Equal(TimeSpan.FromSeconds(3), exception.RetryAfter);
        Assert.DoesNotContain("private provider diagnostic", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingGroqKeyIsReportedAsUnconfigured()
    {
        var configuration = new ConfigurationBuilder().Build();
        var provider = CreateGroqProvider(new HttpClient(), configuration);

        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            provider.GenerateJsonAsync("system", "prompt", CancellationToken.None));

        Assert.False(provider.IsConfigured);
        Assert.Equal(AnalysisFailureCode.NotConfigured, exception.FailureCode);
    }

    [Fact]
    public void GroqKeyAloneDoesNotEnablePricedModelInFreeOnlyMode()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GROQ_API_KEY"] = "test-key",
                ["AI_FREE_ONLY"] = "true"
            })
            .Build();
        var provider = CreateGroqProvider(new HttpClient(), configuration);
        var policy = new AiProviderConfiguration(configuration);

        Assert.True(provider.IsConfigured);
        Assert.False(policy.IsAllowedInFreeOnlyMode(provider.ProviderId));
    }

    [Fact]
    public void FlatEnvironmentVariablesConfigureModelGenerationLimits()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI_TEMPERATURE"] = "0.7",
                ["AI_MAX_OUTPUT_TOKENS"] = "4096"
            })
            .Build();

        Assert.Equal(0.7, AiProviderConfiguration.ReadTemperature(configuration));
        Assert.Equal(4096, AiProviderConfiguration.ReadMaxOutputTokens(configuration));
    }

    [Fact]
    public async Task ProviderAuthenticationFailureIsSafeAndEligibleForFailover()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GROQ_API_KEY"] = "test-key"
            })
            .Build();
        var provider = CreateGroqProvider(
            new HttpClient(new ProviderHandler(
                HttpStatusCode.Unauthorized,
                """{"error":{"message":"private authentication diagnostic"}}""")),
            configuration);

        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            provider.GenerateJsonAsync("system", "prompt", CancellationToken.None));

        Assert.Equal(AnalysisFailureCode.ProviderAuthentication, exception.FailureCode);
        Assert.False(exception.Retryable);
        Assert.DoesNotContain("private authentication diagnostic", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderNetworkTimeoutIsMarkedForRetryAndFallback()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GROQ_API_KEY"] = "test-key"
            })
            .Build();
        var provider = CreateGroqProvider(
            new HttpClient(new ExceptionHandler(new TaskCanceledException("private timeout detail"))),
            configuration);

        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            provider.GenerateJsonAsync("system", "prompt", CancellationToken.None));

        Assert.True(exception.Retryable);
        Assert.DoesNotContain("private timeout detail", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DigestResultExposesBackwardCompatibleAndStandardizedResponseFields()
    {
        var report = new AiAnalysisReport { ExecutiveSummary = "Reviewed." };
        var result = new DigestResult
        {
            AnalysisProvider = "Gemini / gemini-3.1-flash-lite",
            ExecutiveSummary = "Reviewed.",
            AiReport = report,
            ModelInfo = new AiModelInfo
            {
                Provider = "Google Gemini",
                ModelName = "gemini-3.1-flash-lite",
                AnalysisStartedAt = DateTimeOffset.UtcNow,
                AnalysisCompletedAt = DateTimeOffset.UtcNow.AddMilliseconds(25)
            }
        };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var document = JsonDocument.Parse(json);

        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Google Gemini", document.RootElement.GetProperty("provider").GetString());
        Assert.Equal("gemini-3.1-flash-lite", document.RootElement.GetProperty("model").GetString());
        Assert.Equal("Reviewed.", document.RootElement.GetProperty("summary").GetString());
        Assert.True(document.RootElement.GetProperty("processingTime").GetDouble() > 0);
        Assert.True(document.RootElement.TryGetProperty("analysis", out _));
        Assert.True(document.RootElement.TryGetProperty("keyFindings", out _));
        Assert.True(document.RootElement.TryGetProperty("recommendations", out _));
    }

    [Fact]
    public async Task AnalysisCacheIsScopedToUserAndConsentAndCoalescesConcurrentRequests()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var configuration = new ConfigurationBuilder().Build();
        var cache = new AnalysisResultCache(memoryCache, configuration);
        var calls = 0;
        var gate = new TaskCompletionSource<DigestResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Task<DigestResult> Create()
        {
            Interlocked.Increment(ref calls);
            return gate.Task;
        }

        var first = cache.GetOrCreateAsync("user-a", "doc.md", "content", null, null, true, Create);
        var duplicate = cache.GetOrCreateAsync("user-a", "doc.md", "content", null, null, true, Create);
        gate.SetResult(new DigestResult { DocumentTitle = "doc.md" });
        await Task.WhenAll(first, duplicate);
        Assert.Equal(1, calls);

        await cache.GetOrCreateAsync(
            "user-b", "doc.md", "content", null, null, true,
            () => Task.FromResult(new DigestResult()));
        await cache.GetOrCreateAsync(
            "user-a", "doc.md", "content", null, null, false,
            () => Task.FromResult(new DigestResult()));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task AnalysisCacheSeparatesAnalysisDepths()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var configuration = new ConfigurationBuilder().Build();
        var cache = new AnalysisResultCache(memoryCache, configuration);
        var calls = 0;
        Task<DigestResult> Create()
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new DigestResult());
        }

        await cache.GetOrCreateAsync("user-a", "doc.md", "content", null, null, true, Create, "Quick");
        await cache.GetOrCreateAsync("user-a", "doc.md", "content", null, null, true, Create, "Smart");
        await cache.GetOrCreateAsync("user-a", "doc.md", "content", null, null, true, Create, "Quick");

        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CancellingOneDuplicateAnalysisDoesNotCancelOtherWaiters()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = new AnalysisResultCache(memoryCache, new ConfigurationBuilder().Build());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<DigestResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var firstProgress = new List<AnalysisProgressEvent>();
        var secondProgress = new List<AnalysisProgressEvent>();
        async Task<DigestResult> Create(
            CancellationToken cancellationToken,
            IProgress<AnalysisProgressEvent>? progress)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            progress?.Report(new AnalysisProgressEvent("AI inference", CompletedInferenceCalls: 1));
            return await release.Task.WaitAsync(cancellationToken);
        }

        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        Func<CancellationToken, IProgress<AnalysisProgressEvent>?, Task<DigestResult>> factory = Create;
        var first = cache.GetOrCreateAsync(
            "user-a", "doc.md", "content", null, null, true,
            factory, "Quick", firstCancellation.Token,
            new InlineProgress<AnalysisProgressEvent>(firstProgress.Add));
        var second = cache.GetOrCreateAsync(
            "user-a", "doc.md", "content", null, null, true,
            factory, "Quick", secondCancellation.Token,
            new InlineProgress<AnalysisProgressEvent>(secondProgress.Add));
        await started.Task;

        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(second.IsCompleted);
        release.SetResult(new DigestResult { DocumentTitle = "doc.md" });

        Assert.Equal("doc.md", (await second).DocumentTitle);
        Assert.Equal(1, calls);
        Assert.Contains(secondProgress, item => item.Stage == "AI inference");
    }

    [Fact]
    public async Task SharedAnalysisCancelsWhenItsLastWaiterDisconnects()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = new AnalysisResultCache(memoryCache, new ConfigurationBuilder().Build());
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<DigestResult> Create(CancellationToken cancellationToken)
        {
            started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The delay should only end through cancellation.");
            }
            catch (OperationCanceledException)
            {
                providerCancelled.TrySetResult();
                throw;
            }
        }

        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        var first = cache.GetOrCreateAsync(
            "user-a", "doc.md", "same", null, null, true,
            Create, "Quick", firstCancellation.Token);
        var second = cache.GetOrCreateAsync(
            "user-a", "doc.md", "same", null, null, true,
            Create, "Quick", secondCancellation.Token);
        await started.Task;

        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.False(providerCancelled.Task.IsCompleted);
        secondCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        await providerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PresentationSourceCanReuseCompletedAnalysisContext()
    {
        var sources = new PresentationSourceProviders(
            new HttpClient(),
            new ConfigurationBuilder().Build());
        var context = """{"executiveSummary":"A tested analysis.","keyFindings":[]}""";

        var source = await sources.GetSourceAsync(
            new PresentationRequest
            {
                SourceType = "AnalysisResult",
                SourceReference = "policy.md",
                AnalysisContext = context
            },
            null,
            CancellationToken.None);

        Assert.Equal("AnalysisResult", source.SourceType);
        Assert.Equal("policy.md", source.SourceReference);
        Assert.Equal(context, source.Text);
    }

    [Fact]
    public void DailyUsageLimitIsPerClientAndResetsOnNextUtcDate()
    {
        var limiter = new AiDailyUsageLimiter();
        var today = new DateOnly(2026, 6, 18);

        Assert.True(limiter.TryConsume("client-a", 1, today));
        Assert.False(limiter.TryConsume("client-a", 1, today));
        Assert.True(limiter.TryConsume("client-b", 1, today));
        Assert.True(limiter.TryConsume("client-a", 1, today.AddDays(1)));
    }

    private static GroqAnalysisProvider CreateGroqProvider(
        HttpClient httpClient,
        IConfiguration configuration) =>
        new(httpClient, configuration, new AiProviderConfiguration(configuration));

    private sealed class ProviderHandler(
        HttpStatusCode status,
        string responseBody,
        TimeSpan? retryAfter = null) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Authorization { get; private set; }
        public string? GoogleApiKey { get; private set; }
        public string? Model { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            Authorization = request.Headers.Authorization?.ToString();
            GoogleApiKey = request.Headers.TryGetValues("x-goog-api-key", out var keys)
                ? keys.Single()
                : null;
            if (request.Content is not null)
            {
                using var requestJson = await JsonDocument.ParseAsync(
                    await request.Content.ReadAsStreamAsync(cancellationToken),
                    cancellationToken: cancellationToken);
                Model = requestJson.RootElement.GetProperty("model").GetString();
            }
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
            if (retryAfter is { } value)
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(value);
            return response;
        }

    }

    private sealed class ExceptionHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class StaticAiModelClient(string content) : IAiModelClient
    {
        public string ProviderId => "Gemini";
        public string ProviderName => "Google Gemini";
        public string ModelName => "gemini-3.1-flash-lite";

        public Task<ModelInferenceResult> GenerateJsonAsync(
            string systemInstruction,
            string prompt,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ModelInferenceResult(
                content, ModelName, 10, 5));
    }
}
