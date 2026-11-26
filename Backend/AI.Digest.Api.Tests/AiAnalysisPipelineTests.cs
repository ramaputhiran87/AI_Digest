using System.Text.Json;
using AI.Digest.Api.Models;
using AI.Digest.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AI.Digest.Api.Tests;

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
    public async Task RejectsModelEvidenceThatIsNotPresentInTheDocument()
    {
        var client = new MockAiModelClient(inventEvidence: true);
        var service = CreateService(client);

        var exception = await Assert.ThrowsAsync<AnalysisProviderException>(() =>
            service.AnalyseAsync("architecture.md", Source, null));

        Assert.Contains("unsupported evidence", exception.Message, StringComparison.OrdinalIgnoreCase);
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

    [Fact(Skip = "Requires Gemini:ApiKey and intentional live provider access. See README live-connectivity instructions.")]
    public async Task GeminiProviderConnectivity()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var client = new GeminiAnalysisProvider(new HttpClient(), configuration);
        var result = await client.GenerateJsonAsync(
            "Return JSON only.",
            """Return {"connected":true}.""",
            CancellationToken.None);

        using var json = JsonDocument.Parse(result.Content);
        Assert.True(json.RootElement.GetProperty("connected").GetBoolean());
    }

    [Fact(Skip = "Requires a running Ollama service and pulled Ollama:Model. See README live-connectivity instructions.")]
    public async Task OllamaProviderConnectivity()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var client = new OllamaAnalysisProvider(new HttpClient(), configuration);
        var result = await client.GenerateJsonAsync(
            "Return JSON only.",
            """Return {"connected":true}.""",
            CancellationToken.None);

        using var json = JsonDocument.Parse(result.Content);
        Assert.True(json.RootElement.GetProperty("connected").GetBoolean());
    }

    private static DocumentAnalysisService CreateService(MockAiModelClient client)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Analysis:Provider"] = "Gemini",
            ["Analysis:ChunkCharacters"] = "12000",
            ["Analysis:MaxChunks"] = "40",
            ["Analysis:MaxParallelCalls"] = "4"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var aiProvider = new LlmAnalysisProvider(configuration, [client]);
        var router = new AnalysisProviderRouter(
            configuration,
            aiProvider,
            new DeterministicAnalysisProvider(new AgentServices()));
        return new DocumentAnalysisService(router);
    }

    private sealed class MockAiModelClient(
        bool inventEvidence = false,
        bool noFindings = false) : IAiModelClient
    {
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
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref callCount);
            var content = prompt.Contains("Analysis stage: Document Understanding Agent", StringComparison.Ordinal)
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
