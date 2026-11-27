using AI.Digest.Api.Models;
using System.Net;

namespace AI.Digest.Api.Services;

public interface IAnalysisProvider
{
    Task<ProviderAnalysisResult> AnalyseAsync(
        ParsedDocument document,
        string? useCase,
        CancellationToken cancellationToken,
        string? explicitMode = null,
        bool allowCloudProcessing = false,
        string? analysisDepth = null,
        IProgress<AnalysisProgressEvent>? progress = null);
}

public sealed record ProviderAnalysisResult(
    string ProviderName,
    List<Finding> Findings,
    List<ContradictionFindingDetail> Contradictions,
    List<Finding> HumanReview,
    List<Finding> Recommendations,
    List<Finding> HiddenRisks,
    List<Finding> FutureRisks,
    List<Finding> Opportunities,
    List<Finding> SecurityFindings,
    List<Finding> ComplianceFindings,
    string? ExecutiveSummary)
{
    public AiAnalysisReport? AiReport { get; init; }
    public AiModelInfo? ModelInfo { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public sealed class AnalysisProviderException : Exception
{
    public AnalysisProviderException(
        string message,
        Exception? innerException = null,
        bool retryable = false,
        TimeSpan? retryAfter = null,
        string? providerId = null,
        AnalysisFailureCode failureCode = AnalysisFailureCode.ProviderUnavailable,
        HttpStatusCode? statusCode = null)
        : base(message, innerException)
    {
        Retryable = retryable;
        RetryAfter = retryAfter;
        ProviderId = providerId;
        FailureCode = failureCode;
        StatusCode = statusCode;
    }

    public bool Retryable { get; }
    public TimeSpan? RetryAfter { get; }
    public string? ProviderId { get; }
    public AnalysisFailureCode FailureCode { get; }
    public HttpStatusCode? StatusCode { get; }
}

public sealed class AnalysisProviderRouter(
    IConfiguration configuration,
    LlmAnalysisProvider aiProvider,
    DeterministicAnalysisProvider deterministicProvider,
    AiProviderConfiguration providerConfiguration,
    IEnumerable<IAiModelClient> modelClients) : IAnalysisProvider
{
    public async Task<ProviderAnalysisResult> AnalyseAsync(
        ParsedDocument document,
        string? useCase,
        CancellationToken cancellationToken,
        string? explicitMode = null,
        bool allowCloudProcessing = false,
        string? analysisDepth = null,
        IProgress<AnalysisProgressEvent>? progress = null)
    {
        var mode = string.IsNullOrWhiteSpace(explicitMode)
            ? configuration["Analysis:Provider"] ?? "Configured"
            : explicitMode.Trim();

        if (mode.Equals("Deterministic", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(explicitMode))
            {
                throw new AnalysisProviderException(
                    "Rules-based analysis is available only when explicitly selected.");
            }

            return await deterministicProvider.AnalyseAsync(document, useCase, cancellationToken);
        }

        if (!providerConfiguration.Enabled)
        {
            throw new AnalysisProviderException(
                "AI analysis is disabled by configuration.",
                failureCode: AnalysisFailureCode.Disabled);
        }

        var clients = modelClients.ToDictionary(client => client.ProviderId,
            StringComparer.OrdinalIgnoreCase);
        var orderedClients = providerConfiguration.ProviderOrder
            .Where(clients.ContainsKey)
            .Select(providerId => clients[providerId])
            .ToList();
        var credentialsConfigured = orderedClients
            .Where(client => client.IsConfigured)
            .ToList();
        var supported = credentialsConfigured
            .Where(client => providerConfiguration.HasSupportedModel(client.ProviderId))
            .ToList();
        var configured = supported
            .Where(client => providerConfiguration.IsEnabledFromConfiguration(client.ProviderId))
            .ToList();
        var freeEligible = configured
            .Where(client => providerConfiguration.IsAllowedInFreeOnlyMode(client.ProviderId))
            .ToList();
        var eligible = freeEligible
            .Where(client => !providerConfiguration.CloudConsentRequired ||
                             allowCloudProcessing ||
                             !providerConfiguration.IsCloud(client.ProviderId))
            .OrderBy(client => providerConfiguration.Priority(client.ProviderId))
            .ToList();

        if (!mode.Equals("Configured", StringComparison.OrdinalIgnoreCase))
        {
            if (!clients.TryGetValue(mode, out var selected))
                throw new AnalysisProviderException(
                    $"Unsupported analysis provider '{mode}'.",
                    failureCode: AnalysisFailureCode.InvalidInput);

            if (providerConfiguration.IsConfigured(selected.ProviderId, selected) &&
                providerConfiguration.IsAllowedInFreeOnlyMode(selected.ProviderId) &&
                (!providerConfiguration.CloudConsentRequired ||
                 allowCloudProcessing ||
                 !providerConfiguration.IsCloud(selected.ProviderId)))
            {
                eligible = eligible
                    .Where(client => !client.ProviderId.Equals(
                        selected.ProviderId, StringComparison.OrdinalIgnoreCase))
                    .Prepend(selected)
                    .ToList();
            }
        }

        if (!providerConfiguration.FallbackEnabled && eligible.Count > 1)
            eligible = eligible.Take(1).ToList();

        if (eligible.Count == 0)
        {
            var failureCode = credentialsConfigured.Count == 0
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
                failureCode: failureCode);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(providerConfiguration.RequestTimeoutSeconds));
        try
        {
            return await aiProvider.AnalyseAsync(
            document,
            useCase,
            eligible[0].ProviderId,
            allowCloudProcessing,
            timeout.Token,
            analysisDepth,
            progress);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AnalysisProviderException(
                "The analysis request exceeded its configured timeout.",
                failureCode: AnalysisFailureCode.RequestTimeout);
        }
    }
}

public sealed class DeterministicAnalysisProvider(AgentServices agents)
{
    public Task<ProviderAnalysisResult> AnalyseAsync(
        ParsedDocument document,
        string? useCase,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requirement = agents.RequirementAgent(document);
        var security = agents.SecurityAgent(document);
        var compliance = agents.ComplianceAgent(document);
        var impact = agents.BusinessImpactAgent(document);
        var human = agents.HumanReviewAgent(document);
        var contradictions = agents.ContradictionAgent(document);
        var opportunities = agents.DreamAgent(document);
        var hiddenRisks = agents.DarkMatterAgent(document);
        var futureRisks = agents.TimeMachineAgent(document);
        var recommendations = agents.RecommendationAgent(document);
        var criticalRisks = agents.CriticalRiskAgent(document);
        var highPriority = agents.HighPriorityAgent(document);

        var findings = requirement
            .Concat(security)
            .Concat(compliance)
            .Concat(impact)
            .Concat(human)
            .Concat(opportunities)
            .Concat(hiddenRisks)
            .Concat(futureRisks)
            .Concat(recommendations)
            .Concat(criticalRisks)
            .Concat(highPriority)
            .ToList();

        return Task.FromResult(new ProviderAnalysisResult(
            "Deterministic rules (explicit fallback)",
            findings,
            contradictions,
            human,
            recommendations,
            hiddenRisks,
            futureRisks,
            opportunities,
            security,
            compliance,
            null));
    }
}
