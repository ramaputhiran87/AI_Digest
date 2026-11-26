using System.Text.Json;
using System.Net;
using System.Text;
using AI.Digest.Api.Models;
using AI.Digest.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AI.Digest.Api.Tests;

public sealed class PresentationExportTests
{
    [Fact]
    public void GeneratesEditablePptxAndConsistentPdf()
    {
        var presentation = new PresentationDocument
        {
            Id = Guid.NewGuid(),
            Title = "AI Digest Architecture",
            Description = "Enterprise document intelligence overview",
            SourceType = "UseCase",
            TargetAudience = "Engineering leaders",
            Theme = "AI_Digest Navy Blue",
            AspectRatio = "16:9",
            TotalSlides = 2,
            AIProvider = "Test Provider",
            AIModel = "Test Model",
            Status = "Completed",
            Slides =
            [
                new PresentationSlide
                {
                    SlideNumber = 1,
                    Title = "AI Digest",
                    Subtitle = "Enterprise document intelligence",
                    LayoutType = "Title",
                    Content = ["Angular", ".NET 10", "Gemini or Ollama"],
                    SpeakerNotes = "Introduce the platform.",
                    SourceReferences = ["Use-case description"]
                },
                new PresentationSlide
                {
                    SlideNumber = 2,
                    Title = "Intelligent workflow",
                    LayoutType = "Workflow",
                    Content = ["Document extraction", "LLM understanding", "Evidence validation", "Presentation export"],
                    SpeakerNotes = "Explain the end-to-end workflow.",
                    Diagram = new PresentationDiagram
                    {
                        Type = "Workflow",
                        Mermaid = "flowchart TD\n A[Document] --> B[LLM] --> C[Slides]"
                    }
                }
            ]
        };

        var generator = new PresentationGenerationService();
        var pptx = generator.GeneratePptx(presentation);
        var pdf = generator.GeneratePdf(presentation);

        Assert.True(pptx.Length > 1000);
        Assert.True(pdf.Length > 1000);
        Assert.Equal((byte)'P', pptx[0]);
        Assert.Equal((byte)'K', pptx[1]);
        Assert.Equal((byte)'%', pdf[0]);
    }
}

public sealed class OpenAiAnalysisProviderTests
{
    [Fact]
    public async Task UsesResponsesApiAndExtractsStructuredOutput()
    {
        var handler = new CapturingHandler();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenAI:ApiKey"] = "test-key",
                ["OpenAI:Model"] = "gpt-6.2-astra"
            })
            .Build();
        var provider = new OpenAiAnalysisProvider(new HttpClient(handler), configuration);

        var result = await provider.GenerateJsonAsync("system instruction", "review prompt", CancellationToken.None);

        Assert.Equal("OpenAI", provider.ProviderId);
        Assert.Equal("gpt-6.2-astra", provider.ModelName);
        Assert.Equal("https://api.openai.com/v1/responses", handler.RequestUri?.ToString());
        Assert.Equal("Bearer test-key", handler.Authorization);
        Assert.Equal("{\"reviewed\":true}", result.Content);
        Assert.Equal("gpt-6.2-astra", result.ModelVersion);
        Assert.Equal(12, result.InputTokens);
        Assert.Equal(4, result.OutputTokens);

        using var requestJson = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("review prompt", requestJson.RootElement.GetProperty("input").GetString());
        Assert.Equal("system instruction", requestJson.RootElement.GetProperty("instructions").GetString());
        Assert.Equal("json_object",
            requestJson.RootElement.GetProperty("text").GetProperty("format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task RequiresAnOpenAiApiKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var provider = new OpenAiAnalysisProvider(new HttpClient(), configuration);

        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            provider.GenerateJsonAsync("system", "prompt", CancellationToken.None));

        Assert.Contains("credentials are not configured", exception.Message, StringComparison.Ordinal);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? Authorization { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "model": "gpt-6.2-astra",
                      "output": [
                        {
                          "type": "message",
                          "content": [
                            { "type": "output_text", "text": "{\"reviewed\":true}" }
                          ]
                        }
                      ],
                      "usage": { "input_tokens": 12, "output_tokens": 4 }
                    }
                    """,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}

public sealed class AiAnalysisPipelineTests
{
    private const string Source = """
        # Service Architecture

        The API stores audit events for 90 days.
        """;

    [Fact]
    public async Task UsesMockModelForUnderstandingSpecialistsAndSynthesis()
    {
        var client = new MockAiModelClient();
        var service = CreateService(client);

        var result = await service.AnalyseAsync("architecture.md", Source, null);

        Assert.Equal(7, client.CallCount);
        Assert.Equal(7, result.ModelInfo?.InferenceCalls);
        Assert.Equal("Mock Gemini", result.ModelInfo?.Provider);
        Assert.Single(result.AiReport!.Findings);
        Assert.Equal("MEDIUM", result.AiReport.Findings[0].Severity);
        Assert.Equal(3, result.AiReport.Findings[0].Evidence[0].StartLine);
        Assert.True(result.KeyFindings[0].Evidence!.SourceText.Contains(
            "The API stores audit events", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FreeTierModeCombinesSpecialistsAndRetainsEvidenceValidation()
    {
        var client = new MockAiModelClient();
        var service = CreateService(client, freeTierMode: true);

        var result = await service.AnalyseAsync("architecture.md", Source, null);

        Assert.Equal(2, client.CallCount);
        Assert.Equal(2, result.ModelInfo?.InferenceCalls);
        Assert.Single(result.AiReport!.Findings);
        Assert.Equal(3, result.AiReport.Findings[0].Evidence[0].StartLine);
    }

    [Fact]
    public async Task RejectsModelEvidenceThatIsNotPresentInTheDocument()
    {
        var client = new MockAiModelClient(inventEvidence: true);
        var service = CreateService(client);

        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            service.AnalyseAsync("architecture.md", Source, null));

        Assert.Contains(
            "unsupported evidence",
            exception.InnerException?.Message ?? exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptsAValidReportWithNoFindings()
    {
        var client = new MockAiModelClient(noFindings: true);
        var service = CreateService(client);

        var result = await service.AnalyseAsync(
            "application-config.json",
            """{"apiKey":"YOUR_API_KEY"}""",
            null);

        Assert.Empty(result.AiReport!.Findings);
        Assert.Contains("placeholder", result.AiReport.SecurityAssessment, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FallsBackToTheNextConfiguredProviderAfterPrimaryFailure()
    {
        var fallback = new MockAiModelClient(providerId: "Groq");
        var primary = new FailingAiModelClient("Gemini");
        var settings = new Dictionary<string, string?>
        {
            ["Analysis:Provider"] = "Configured",
            ["Analysis:ChunkCharacters"] = "12000",
            ["Analysis:MaxChunks"] = "40",
            ["Analysis:MaxParallelCalls"] = "1",
            ["AI:FreeOnly"] = "false",
            ["AI:CloudConsentRequired"] = "false",
            ["AI:RetryLimit"] = "0"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var clients = new IAiModelClient[] { primary, fallback };
        var router = new AnalysisProviderRouter(
            configuration,
            CreateLlmProvider(configuration, clients),
            new DeterministicAnalysisProvider(new AgentServices()),
            new AiProviderConfiguration(configuration),
            clients);
        var service = new DocumentAnalysisService(router);

        var result = await service.AnalyseAsync(
            "architecture.md", Source, null, explicitMode: "Gemini");

        Assert.Equal("Mock Groq", result.ModelInfo?.Provider);
        Assert.Equal(3, primary.CallCount);
        Assert.Equal(7, fallback.CallCount);
    }

    [Fact]
    public async Task SwitchesFromGeminiToGroqWhenGeminiIsRateLimited()
    {
        var fallback = new MockAiModelClient(providerId: "Groq");
        var primary = new QuotaLimitedAiModelClient("Gemini");
        var configuration = CreateFallbackConfiguration();
        var clients = new IAiModelClient[] { primary, fallback };
        var router = new AnalysisProviderRouter(
            configuration,
            CreateLlmProvider(configuration, clients),
            new DeterministicAnalysisProvider(new AgentServices()),
            new AiProviderConfiguration(configuration),
            clients);

        var result = await new DocumentAnalysisService(router).AnalyseAsync(
            "architecture.md", Source, null, explicitMode: "Gemini");

        Assert.Equal("Mock Groq", result.ModelInfo?.Provider);
        Assert.Equal(1, primary.CallCount);
        Assert.Equal(7, fallback.CallCount);
    }

    [Fact]
    public async Task RecordsBothProvidersWhenFallbackOccursDuringSpecialistAnalysis()
    {
        var primary = new FailsAfterFirstInferenceAiModelClient();
        var fallback = new MockAiModelClient(providerId: "Groq");
        var configuration = CreateFallbackConfiguration();
        var clients = new IAiModelClient[] { primary, fallback };
        var router = new AnalysisProviderRouter(
            configuration,
            CreateLlmProvider(configuration, clients),
            new DeterministicAnalysisProvider(new AgentServices()),
            new AiProviderConfiguration(configuration),
            clients);

        var result = await new DocumentAnalysisService(router).AnalyseAsync(
            "architecture.md", Source, null, explicitMode: "Gemini");

        Assert.Contains("Mock Gemini", result.ModelInfo!.Provider, StringComparison.Ordinal);
        Assert.Contains("Mock Groq", result.ModelInfo.Provider, StringComparison.Ordinal);
        Assert.Contains(result.ModelInfo.ProviderUsage, usage =>
            usage.Provider == "Mock Gemini" && usage.InferenceCalls > 1);
        Assert.Contains(result.ModelInfo.ProviderUsage, usage =>
            usage.Provider == "Mock Groq" && usage.InferenceCalls > 0);
    }

    [Fact]
    public async Task ReturnsDailyLimitWhenEveryEligibleProviderIsRateLimited()
    {
        var clients = new IAiModelClient[]
        {
            new QuotaLimitedAiModelClient("Gemini"),
            new QuotaLimitedAiModelClient("Groq")
        };
        var configuration = CreateFallbackConfiguration();
        var router = new AnalysisProviderRouter(
            configuration,
            CreateLlmProvider(configuration, clients),
            new DeterministicAnalysisProvider(new AgentServices()),
            new AiProviderConfiguration(configuration),
            clients);

        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            new DocumentAnalysisService(router).AnalyseAsync(
                "architecture.md", Source, null, explicitMode: "Gemini"));

        Assert.Equal(AnalysisFailureCode.DailyLimitReached, exception.FailureCode);
    }

    [Fact]
    public async Task FallsBackWhenPrimaryProviderReturnsMalformedModelJson()
    {
        var fallback = new MockAiModelClient(providerId: "Groq");
        var primary = new InvalidJsonAiModelClient("Gemini");
        var configuration = CreateFallbackConfiguration();
        var clients = new IAiModelClient[] { primary, fallback };
        var router = new AnalysisProviderRouter(
            configuration,
            CreateLlmProvider(configuration, clients),
            new DeterministicAnalysisProvider(new AgentServices()),
            new AiProviderConfiguration(configuration),
            clients);

        var result = await new DocumentAnalysisService(router).AnalyseAsync(
            "architecture.md", Source, null, explicitMode: "Gemini");

        Assert.Equal("Mock Groq", result.ModelInfo?.Provider);
        Assert.Equal(3, primary.CallCount);
        Assert.Equal(7, fallback.CallCount);
    }

    [Fact]
    public async Task RetriesProviderTimeoutBeforeFailingOver()
    {
        var fallback = new MockAiModelClient(providerId: "Groq");
        var primary = new TimeoutAiModelClient("Gemini");
        var settings = new Dictionary<string, string?>
        {
            ["Analysis:Provider"] = "Configured",
            ["Analysis:ChunkCharacters"] = "12000",
            ["Analysis:MaxChunks"] = "40",
            ["Analysis:MaxParallelCalls"] = "1",
            ["AI:FreeOnly"] = "false",
            ["AI:CloudConsentRequired"] = "false",
            ["AI:ProviderTimeoutSeconds"] = "1",
            ["AI:RetryLimit"] = "1"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var clients = new IAiModelClient[] { primary, fallback };
        var router = new AnalysisProviderRouter(
            configuration,
            CreateLlmProvider(configuration, clients),
            new DeterministicAnalysisProvider(new AgentServices()),
            new AiProviderConfiguration(configuration),
            clients);

        var result = await new DocumentAnalysisService(router).AnalyseAsync(
            "architecture.md", Source, null, explicitMode: "Gemini");

        Assert.Equal("Mock Groq", result.ModelInfo?.Provider);
        Assert.Equal(4, primary.CallCount);
        Assert.Equal(7, fallback.CallCount);
    }

    [Fact]
    public async Task ReturnsUnavailableWhenEveryEligibleProviderFails()
    {
        var clients = new IAiModelClient[]
        {
            new FailingAiModelClient("Groq"),
            new FailingAiModelClient("Gemini")
        };
        var configuration = CreateFallbackConfiguration();
        var router = new AnalysisProviderRouter(
            configuration,
            CreateLlmProvider(configuration, clients),
            new DeterministicAnalysisProvider(new AgentServices()),
            new AiProviderConfiguration(configuration),
            clients);

        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            new DocumentAnalysisService(router).AnalyseAsync(
                "architecture.md", Source, null, explicitMode: "Groq"));

        Assert.Equal(AnalysisFailureCode.ProviderUnavailable, exception.FailureCode);
        Assert.Contains("All eligible AI providers", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsEmptyDocumentBeforeCallingAnyProvider()
    {
        var client = new MockAiModelClient();
        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            CreateService(client).AnalyseAsync("empty.txt", " \r\n ", null));

        Assert.Equal(AnalysisFailureCode.InvalidInput, exception.FailureCode);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void FreeOnlyModeAllowsConfirmedGeminiAndBlocksThePricedGroqModel()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI_FREE_ONLY"] = "true",
                ["GEMINI_MODEL"] = "gemini-3.1-flash-lite",
                ["GEMINI_FREE_TIER_CONFIRMED"] = "true",
                ["GROQ_MODEL"] = "openai/gpt-oss-120b"
            })
            .Build();
        var policy = new AiProviderConfiguration(configuration);

        Assert.True(policy.IsAllowedInFreeOnlyMode("Gemini"));
        Assert.False(policy.IsAllowedInFreeOnlyMode("Groq"));
    }

    [Fact]
    public void FlatEnvironmentVariablesBindProviderModelsAndPriority()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI_ENABLED"] = "true",
                ["AI_FREE_ONLY"] = "true",
                ["AI_FALLBACK_ENABLED"] = "true",
                ["AI_PROVIDER_ORDER"] = "groq,gemini",
                ["GEMINI_ENABLED"] = "false",
                ["GEMINI_MODEL"] = "gemini-3.1-flash-lite",
                ["GROQ_ENABLED"] = "true",
                ["GROQ_MODEL"] = "openai/gpt-oss-120b"
            })
            .Build();
        var policy = new AiProviderConfiguration(configuration);

        Assert.True(policy.Enabled);
        Assert.True(policy.FallbackEnabled);
        Assert.False(policy.IsEnabled("Gemini"));
        Assert.True(policy.IsEnabled("Groq"));
        Assert.Equal(0, policy.Priority("Groq"));
        Assert.Equal("gemini-3.1-flash-lite", policy.Model("Gemini"));
        Assert.Equal("openai/gpt-oss-120b", policy.Model("Groq"));
    }

    [Fact(Skip = "Requires GEMINI_API_KEY and intentional live provider access. See README live-connectivity instructions.")]
    public async Task GeminiProviderConnectivity()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var client = new GeminiAnalysisProvider(
            new HttpClient(), configuration, new AiProviderConfiguration(configuration));
        var result = await client.GenerateJsonAsync(
            "Return JSON only.",
            """Return {"connected":true}.""",
            CancellationToken.None);

        using var json = JsonDocument.Parse(result.Content);
        Assert.True(json.RootElement.GetProperty("connected").GetBoolean());
    }

    [Fact(Skip = "Requires GROQ_API_KEY and intentional live provider access. The requested model is priced by Groq.")]
    public async Task GroqProviderConnectivity()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var client = new GroqAnalysisProvider(
            new HttpClient(), configuration, new AiProviderConfiguration(configuration));
        var result = await client.GenerateJsonAsync(
            "Return JSON only.",
            """Return {"connected":true}.""",
            CancellationToken.None);

        using var json = JsonDocument.Parse(result.Content);
        Assert.True(json.RootElement.GetProperty("connected").GetBoolean());
    }

    private static DocumentAnalysisService CreateService(
        MockAiModelClient client,
        string providerName = "Gemini",
        bool freeTierMode = false)
    {
        var configuration = CreateFallbackConfiguration(providerName, freeTierMode);
        var clients = new IAiModelClient[] { client };
        var aiProvider = CreateLlmProvider(configuration, clients);
        var router = new AnalysisProviderRouter(
            configuration,
            aiProvider,
            new DeterministicAnalysisProvider(new AgentServices()),
            new AiProviderConfiguration(configuration),
            clients);
        return new DocumentAnalysisService(router);
    }

    private static LlmAnalysisProvider CreateLlmProvider(
        IConfiguration configuration,
        IEnumerable<IAiModelClient> clients) =>
        new(
            configuration,
            new AiInferenceRouter(
                new AiProviderConfiguration(configuration),
                clients,
                new AiProviderHealthTracker(),
                NullLogger<AiInferenceRouter>.Instance),
            new AiProviderConfiguration(configuration));

    private static IConfiguration CreateFallbackConfiguration(
        string providerName = "Configured",
        bool freeTierMode = false) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Analysis:Provider"] = providerName,
                ["Analysis:ChunkCharacters"] = "12000",
                ["Analysis:MaxChunks"] = "40",
                ["Analysis:MaxParallelCalls"] = "1",
                ["AI:FreeOnly"] = "false",
                ["AI:CloudConsentRequired"] = "false",
                ["AI:RetryLimit"] = "0",
                ["AI:FreeTierAnalysisMode"] = freeTierMode.ToString()
            })
            .Build();

    private sealed class InvalidJsonAiModelClient(string providerId) : IAiModelClient
    {
        public string ProviderId => providerId;
        public string ProviderName => $"Mock {providerId}";
        public string ModelName => $"{providerId.ToLowerInvariant()}-test";
        public int CallCount { get; private set; }

        public Task<ModelInferenceResult> GenerateJsonAsync(
            string systemInstruction,
            string prompt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new ModelInferenceResult("not-json", null, null, null));
        }
    }

    private sealed class TimeoutAiModelClient(string providerId) : IAiModelClient
    {
        public string ProviderId => providerId;
        public string ProviderName => $"Mock {providerId}";
        public string ModelName => $"{providerId.ToLowerInvariant()}-test";
        public int CallCount { get; private set; }

        public async Task<ModelInferenceResult> GenerateJsonAsync(
            string systemInstruction,
            string prompt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The timeout delay should not complete.");
        }
    }

    private sealed class FailingAiModelClient(string providerId) : IAiModelClient
    {
        public string ProviderId => providerId;
        public string ProviderName => $"Mock {providerId}";
        public string ModelName => $"{providerId.ToLowerInvariant()}-test";
        public int CallCount { get; private set; }

        public Task<ModelInferenceResult> GenerateJsonAsync(
            string systemInstruction,
            string prompt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new AnalysisProviderException("provider unavailable", providerId: providerId);
        }
    }

    private sealed class FailsAfterFirstInferenceAiModelClient : IAiModelClient
    {
        private readonly MockAiModelClient successfulClient = new(providerId: "Gemini");
        private int callCount;

        public string ProviderId => "Gemini";
        public string ProviderName => "Mock Gemini";
        public string ModelName => "gemini-test";
        public int CallCount => callCount;

        public Task<ModelInferenceResult> GenerateJsonAsync(
            string systemInstruction,
            string prompt,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref callCount) > 1)
                throw new AnalysisProviderException(
                    "provider unavailable",
                    providerId: ProviderId);

            return successfulClient.GenerateJsonAsync(
                systemInstruction, prompt, cancellationToken);
        }
    }

    private sealed class QuotaLimitedAiModelClient(string providerId) : IAiModelClient
    {
        public string ProviderId => providerId;
        public string ProviderName => $"Mock {providerId}";
        public string ModelName => $"{providerId.ToLowerInvariant()}-test";
        public int CallCount { get; private set; }

        public Task<ModelInferenceResult> GenerateJsonAsync(
            string systemInstruction,
            string prompt,
            CancellationToken cancellationToken)
        {
            CallCount++;
            throw new AnalysisProviderException(
                "Provider quota exceeded.",
                providerId: providerId,
                failureCode: AnalysisFailureCode.RateLimitExceeded,
                statusCode: HttpStatusCode.TooManyRequests);
        }
    }

    private sealed class MockAiModelClient(
        bool inventEvidence = false,
        bool noFindings = false,
        string providerId = "Gemini") : IAiModelClient
    {
        private int callCount;

        public string ProviderId => providerId;
        public string ProviderName => $"Mock {providerId}";
        public string ModelName => $"{providerId.ToLowerInvariant()}-test";
        public int CallCount => callCount;

        public Task<ModelInferenceResult> GenerateJsonAsync(
            string systemInstruction,
            string prompt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref callCount);
            var content = prompt.Contains("Analysis stage: Free Tier Combined Specialist Analysis", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new
                {
                    understanding = new
                    {
                        purpose = "Describe the service.",
                        domain = "Web services",
                        keyFacts = new[] { "The document describes an API." },
                        components = new[] { "API" },
                        relationships = new[] { "The API records audit events." },
                        summary = "An API architecture document."
                    },
                    assessment = "The source documents a finite audit retention period.",
                    findings = noFindings ? Array.Empty<object>() : new object[]
                    {
                        new
                        {
                            title = "Review audit retention requirements",
                            category = "Security",
                            severity = "MEDIUM",
                            status = "Potential",
                            observation = "The document specifies 90 days of audit event retention.",
                            reasoning = "Retention should align with incident response and regulatory needs.",
                            likelihood = "The documented duration is confirmed, but the probability of operational harm depends on incident needs.",
                            impact = "Loss of older audit events could hinder investigations.",
                            exploitability = "Not applicable; this is a retention-policy observation.",
                            businessImpact = "A short period may limit investigations.",
                            technicalImpact = "Older audit events may no longer be queryable.",
                            recommendation = "Confirm retention against the applicable policies.",
                            evidence = new[]
                            {
                                new
                                {
                                    quotation = inventEvidence ? "Invented source line." : "The API stores audit events for 90 days.",
                                    startLine = 3,
                                    endLine = 3
                                }
                            },
                            confidence = 0.84,
                            severityReason = "Impact depends on the organization's retention obligations."
                        }
                    }
                })
                : prompt.Contains("Analysis stage: Document Understanding Agent", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new
                {
                    purpose = "Describe the service.",
                    domain = "Web services",
                    keyFacts = new[] { "The document describes an API." },
                    components = new[] { "API" },
                    relationships = new[] { "The API records audit events." },
                    summary = "An API architecture document."
                })
                : prompt.Contains("Analysis stage: Security Analysis", StringComparison.Ordinal)
                    ? JsonSerializer.Serialize(new
                    {
                        assessment = "The source documents a finite audit retention period.",
                        findings = noFindings ? Array.Empty<object>() : new object[]
                        {
                            new
                            {
                                title = "Review audit retention requirements",
                                category = "Security",
                                severity = "MEDIUM",
                                status = "Potential",
                                observation = "The document specifies 90 days of audit event retention.",
                                reasoning = "Retention should align with incident response and regulatory needs.",
                                likelihood = "The documented duration is confirmed, but the probability of operational harm depends on incident needs.",
                                impact = "Loss of older audit events could hinder investigations.",
                                exploitability = "Not applicable; this is a retention-policy observation.",
                                businessImpact = "A short period may limit investigations.",
                                technicalImpact = "Older audit events may no longer be queryable.",
                                recommendation = "Confirm retention against the applicable policies.",
                                evidence = new[]
                                {
                                    new
                                    {
                                        quotation = inventEvidence ? "Invented source line." : "The API stores audit events for 90 days.",
                                        startLine = 3,
                                        endLine = 3
                                    }
                                },
                                confidence = 0.84,
                                severityReason = "Impact depends on the organization's retention obligations."
                            }
                        }
                    })
                    : prompt.Contains("Analysis stage: Finding Consolidation", StringComparison.Ordinal)
                        ? BuildReport(inventEvidence, noFindings)
                        : JsonSerializer.Serialize(new { assessment = "No material finding in this specialty.", findings = Array.Empty<object>() });

            return Task.FromResult(new ModelInferenceResult(content, "mock-v1", 10, 5));
        }

        private static string BuildReport(bool inventEvidence, bool noFindings) =>
            JsonSerializer.Serialize(new
            {
                executiveSummary = "The architecture document describes an API with 90-day audit event retention; confirm that this meets the applicable operational and regulatory requirements.",
                documentUnderstanding = new
                {
                    purpose = "Describe a service architecture.",
                    domain = "Web services",
                    keyFacts = new[] { "The API retains audit events for 90 days." },
                    components = new[] { "API" },
                    relationships = new[] { "The API stores audit events." },
                    summary = "An API architecture document."
                },
                securityAssessment = noFindings
                    ? "The supplied value is clearly a placeholder; no security finding is supported."
                    : "Audit event retention is documented; no live vulnerability can be verified from this document.",
                architectureAssessment = "The source provides limited architecture details.",
                complianceAssessment = "The document does not identify a retention policy.",
                businessImpact = "Retention may affect investigations and compliance obligations.",
                findings = noFindings ? Array.Empty<object>() : new object[]
                {
                    new
                    {
                        title = "Review audit retention requirements",
                        category = "Security",
                        severity = "MEDIUM",
                        status = "Potential",
                        observation = "The document specifies 90 days of audit event retention.",
                        reasoning = "Retention should align with incident response and regulatory needs.",
                        likelihood = "The documented duration is confirmed, but the probability of operational harm depends on incident needs.",
                        impact = "Loss of older audit events could hinder investigations.",
                        exploitability = "Not applicable; this is a retention-policy observation.",
                        businessImpact = "A short period may limit investigations.",
                        technicalImpact = "Older audit events may no longer be queryable.",
                        recommendation = "Confirm retention against the applicable policies.",
                        evidence = new[]
                        {
                            new
                            {
                                quotation = inventEvidence ? "Invented source line." : "The API stores audit events for 90 days.",
                                startLine = 3,
                                endLine = 3
                            }
                        },
                        confidence = 0.84,
                        severityReason = "Impact depends on the organization's retention obligations."
                    }
                },
                priorityActionPlan = new[] { "Validate the required audit retention period." },
                overallAssessment = "The source supports a review action, not a confirmed security vulnerability."
            });
    }
}
