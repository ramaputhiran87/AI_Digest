namespace AI.Digest.Api.Models;

public record AnalyseTextRequest(string Text, string? Title, string? UseCase);

public sealed class ParsedDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public string SourceType { get; init; } = "";
    public List<DocumentSection> Sections { get; init; } = [];
    public List<Statement> Statements { get; init; } = [];
    public Dictionary<string, string> Metadata { get; init; } = [];
}

public sealed record DocumentSection(int Index, string Heading, int StartLine, int EndLine, string Text);
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

public sealed class DigestResult
{
    public string DocumentTitle { get; init; } = "";
    public string DocumentType { get; init; } = "";
    public string ExecutiveSummary { get; init; } = "";
    public List<Finding> HighPriorityItems { get; init; } = [];
    public List<Finding> CriticalRisks { get; init; } = [];
    public List<Finding> Dependencies { get; init; } = [];
    public List<ContradictionFinding> Contradictions { get; init; } = [];
    public List<Finding> HumanReviewRequired { get; init; } = [];
    public List<Finding> Recommendations { get; init; } = [];
    public int ConfidenceScore { get; init; }
    public string ConfidenceBand { get; init; } = "";
    public List<EvidenceRef> Evidence { get; init; } = [];
}
