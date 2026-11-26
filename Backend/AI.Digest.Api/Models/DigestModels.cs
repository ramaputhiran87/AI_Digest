namespace AI.Digest.Api.Models;

public record AnalyseTextRequest(
    string Text,
    string? Title,
    string? UseCase,
    string? AnalysisMode = null,
    bool AllowCloudProcessing = false,
    string? CloudConsentScope = null,
    string? CloudConsentPolicyVersion = null);

public sealed class ParsedDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public string SourceType { get; init; } = "";
    public List<DocumentSection> Sections { get; init; } = [];
    public List<DocumentSourceLine> SourceLines { get; init; } = [];
    public List<Statement> Statements { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public sealed record DocumentSection(int Index, string Heading, int StartLine, int EndLine, string Text, int Level = 1);
public sealed record DocumentSourceLine(int LineNumber, string Section, string Content, string Evidence);
public sealed record Statement(
    Guid Id,
    string Text,
    string Section,
    int SourceLineStart,
    int SourceLineEnd,
    string Evidence);

public sealed record EvidenceRef(
    Guid StatementId,
    string SourceText,
    int SourceLineStart,
    int SourceLineEnd,
    string Section);

public sealed class Finding
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FindingType { get; init; } = "";
    public string AgentName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Priority { get; init; } = "Low";
    public int Score { get; init; }
    public int Confidence { get; init; }
    public string ConfidenceBand { get; init; } = "";
    public EvidenceRef? Evidence { get; init; }
}

public sealed record ContradictionFinding(
    Finding Finding,
    EvidenceRef EvidenceA,
    EvidenceRef EvidenceB);

public sealed record ContradictionFindingDetail(
    string Subject,
    string ValueA,
    string ValueB,
    EvidenceRef EvidenceA,
    EvidenceRef EvidenceB,
    int Confidence);

public sealed class CompressionStatistics
{
    public int OriginalLines { get; init; }
    public int CompressedLines { get; init; }
    public int RemovedLines { get; init; }
    public int CompressionPercent { get; init; }
    public int NoiseRemovedPercent { get; init; }
}

public sealed class CompressedDocument
{
    public string FileName { get; init; } = "";
    public string Markdown { get; init; } = "";
    public CompressionStatistics Statistics { get; init; } = new();
}

public sealed class DigestResult
{
    public bool Success { get; init; } = true;
    public string? Error { get; init; }
    public string DocumentTitle { get; init; } = "";
    public string DocumentType { get; init; } = "";
    public string AnalysisProvider { get; init; } = "";
    public AiModelInfo? ModelInfo { get; init; }
    public AiAnalysisReport? AiReport { get; init; }
    public string Provider => ModelInfo?.Provider ?? AnalysisProvider;
    public string Model => ModelInfo?.ModelName ?? "";
    public AiAnalysisReport? Analysis => AiReport;
    public string Summary => ExecutiveSummary;
    public List<string> Warnings { get; init; } = [];
    public double ProcessingTime => ModelInfo is null
        ? 0
        : Math.Max(0, (ModelInfo.AnalysisCompletedAt - ModelInfo.AnalysisStartedAt).TotalMilliseconds);
    public string ExecutiveSummary { get; init; } = "";
    public List<Finding> HighPriorityItems { get; init; } = [];
    public List<Finding> CriticalRisks { get; init; } = [];
    public List<Finding> Dependencies { get; init; } = [];
    public List<ContradictionFinding> Contradictions { get; init; } = [];
    public List<ContradictionFindingDetail> ContradictionDetails { get; init; } = [];
    public List<Finding> HumanReviewRequired { get; init; } = [];
    public List<Finding> Recommendations { get; init; } = [];
    public List<Finding> KeyFindings { get; init; } = [];
    public List<Finding> HiddenRisks { get; init; } = [];
    public List<Finding> FutureRisks { get; init; } = [];
    public List<Finding> StrategicOpportunities { get; init; } = [];
    public List<Finding> ActionItems { get; init; } = [];
    public int ConfidenceScore { get; init; }
    public string ConfidenceBand { get; init; } = "";
    public List<EvidenceRef> Evidence { get; init; } = [];
    public CompressedDocument CompressedDocument { get; init; } = new();
}

public sealed class AiModelInfo
{
    public string Provider { get; init; } = "";
    public string ModelName { get; init; } = "";
    public string? ModelVersion { get; init; }
    public DateTimeOffset AnalysisStartedAt { get; init; }
    public DateTimeOffset AnalysisCompletedAt { get; init; }
    public int InferenceCalls { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public List<AiProviderUsage> ProviderUsage { get; init; } = [];
    public string Status { get; init; } = "Completed";
    public string FindingOrigin { get; init; } = "AI generated; source evidence programmatically checked";
}

public sealed record AiProviderUsage(
    string Provider,
    string ModelName,
    int InferenceCalls,
    int? InputTokens,
    int? OutputTokens);

public sealed class AiAnalysisReport
{
    public string ExecutiveSummary { get; init; } = "";
    public DocumentUnderstanding DocumentUnderstanding { get; init; } = new();
    public string SecurityAssessment { get; init; } = "";
    public string ArchitectureAssessment { get; init; } = "";
    public string ComplianceAssessment { get; init; } = "";
    public string BusinessImpact { get; init; } = "";
    public List<AiFinding> Findings { get; init; } = [];
    public List<string> PriorityActionPlan { get; init; } = [];
    public string OverallAssessment { get; init; } = "";
}

public sealed class DocumentUnderstanding
{
    public string Purpose { get; init; } = "";
    public string Domain { get; init; } = "";
    public List<string> KeyFacts { get; init; } = [];
    public List<string> Components { get; init; } = [];
    public List<string> Relationships { get; init; } = [];
    public string Summary { get; init; } = "";
}

public sealed class AiFinding
{
    public string Title { get; init; } = "";
    public string Category { get; init; } = "";
    public string Severity { get; init; } = "INFORMATIONAL";
    public string Status { get; init; } = "Potential";
    public string Observation { get; init; } = "";
    public string Reasoning { get; init; } = "";
    public string Likelihood { get; init; } = "";
    public string Impact { get; init; } = "";
    public string Exploitability { get; init; } = "";
    public string BusinessImpact { get; init; } = "";
    public string TechnicalImpact { get; init; } = "";
    public string Recommendation { get; init; } = "";
    public List<AiEvidence> Evidence { get; init; } = [];
    public double Confidence { get; init; }
    public string SeverityReason { get; init; } = "";
}

public sealed class AiEvidence
{
    public string Quotation { get; init; } = "";
    public int StartLine { get; init; }
    public int EndLine { get; init; }
}

public sealed record ModelInferenceResult(
    string Content,
    string? ModelVersion,
    int? InputTokens,
    int? OutputTokens);
