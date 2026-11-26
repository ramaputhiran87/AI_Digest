using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public interface IAnalysisProvider
{
    Task<ProviderAnalysisResult> AnalyseAsync(
        ParsedDocument document,
        string? useCase,
        CancellationToken cancellationToken,
        string? explicitMode = null);
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
}

public sealed class AnalysisProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class AnalysisProviderRouter(
    IConfiguration configuration,
    LlmAnalysisProvider aiProvider,
    DeterministicAnalysisProvider deterministicProvider) : IAnalysisProvider
{
    public Task<ProviderAnalysisResult> AnalyseAsync(
        ParsedDocument document,
        string? useCase,
        CancellationToken cancellationToken,
        string? explicitMode = null)
    {
        var mode = string.IsNullOrWhiteSpace(explicitMode)
            ? configuration["Analysis:Provider"] ?? "Gemini"
            : explicitMode.Trim();

        if (mode.Equals("Deterministic", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(explicitMode))
            {
                throw new AnalysisProviderException(
                    "Rules-based analysis is available only when explicitly selected.");
            }

            return deterministicProvider.AnalyseAsync(document, useCase, cancellationToken);
        }

        if (!mode.Equals("Gemini", StringComparison.OrdinalIgnoreCase) &&
            !mode.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            throw new AnalysisProviderException(
                $"Unsupported analysis provider '{mode}'. Select Gemini or Ollama.");
        }

        return aiProvider.AnalyseAsync(document, useCase, mode, cancellationToken);
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
