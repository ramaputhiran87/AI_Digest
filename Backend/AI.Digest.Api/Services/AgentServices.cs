using System.Text.RegularExpressions;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed class AgentServices
{
    private static readonly string[] CriticalTerms =
    [
        "password", "credentials", "secret", "apikey", "api key", "token", "pii", "ssn",
        "gdpr", "pci", "hipaa", "data breach", "vulnerability", "unencrypted", "sha1",
        "outage", "ransomware", "critical", "security approval missing"
    ];

    private static readonly string[] HighTerms =
    [
        "pending approval", "blocker", "mandatory action", "security finding",
        "compliance issue", "missing dependency", "deployment blocker"
    ];

    private static readonly string[] HumanReviewTerms =
    [
        "requires review", "under discussion", "approval required", "unresolved",
        "ownership unclear", "not finalized", "undecided", "requires business decision",
        "pending executive approval"
    ];

    private static readonly Dictionary<string, (string Type, string Priority, int Score)> ExplicitLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Decision"] = ("Decision", "Medium", 60),
            ["Approval"] = ("Approval", "High", 78),
            ["Action"] = ("ActionItem", "Medium", 58),
            ["Risk"] = ("Risk", "High", 82),
            ["Dependency"] = ("Dependency", "Medium", 55),
            ["Blocker"] = ("Blocker", "High", 88),
            ["Security"] = ("Security", "Critical", 95),
            ["Compliance"] = ("Compliance", "Critical", 92),
            ["Requirement"] = ("Requirement", "High", 78),
            ["Recommendation"] = ("Recommendation", "Low", 30),
            ["ProductionImpact"] = ("ProductionImpact", "Critical", 94),
            ["Human Review"] = ("HumanReview", "High", 72)
        };

    public List<Finding> RequirementAgent(ParsedDocument document) =>
        ScanExplicit(document, "RequirementAgent",
            new[] { "Requirement", "Decision", "Approval", "Action", "Dependency", "Blocker" },
            new[] { "must", "shall", "required", "acceptance criteria", "ambiguous", "missing requirement", "incomplete" });

    public List<Finding> SecurityAgent(ParsedDocument document) =>
        ScanPatterns(document, "Security", "SecurityAgent", CriticalTerms,
            new[] { "authentication", "authorization", "encryption", "owasp", "attack", "secret", "password", "credentials" });

    public List<Finding> ComplianceAgent(ParsedDocument document) =>
        ScanPatterns(document, "Compliance", "ComplianceAgent",
            new[] { "gdpr", "hipaa", "pci", "soc2", "governance", "compliance", "regulatory" },
            Array.Empty<string>());

    public List<Finding> BusinessImpactAgent(ParsedDocument document) =>
        ScanPatterns(document, "ProductionImpact", "BusinessImpactAgent",
            new[] { "revenue", "customer", "sla", "outage", "production", "operational", "deployment" },
            Array.Empty<string>());

    public List<Finding> HumanReviewAgent(ParsedDocument document)
    {
        var result = new List<Finding>();
        foreach (var statement in document.Statements)
        {
            var text = statement.Text;
            var isHumanPhrase = HumanReviewTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            var isExplicit = TryGetExplicitLabel(text, out var label) && label.Type == "HumanReview";
            if (!isHumanPhrase && !isExplicit) continue;

            result.Add(BuildFinding(statement, "HumanReview", "HumanReviewAgent",
                isExplicit ? "Explicit human review label" : "Human review phrase detected",
                "High", 72));
        }
        return result;
    }

    public List<ContradictionFindingDetail> ContradictionAgent(ParsedDocument document)
    {
        var contradictions = new List<ContradictionFindingDetail>();
        var statements = document.Statements;

        foreach (var pair in statements.SelectMany((a, i) => statements.Skip(i + 1).Select(b => (A: a, B: b))))
        {
            var subjectAndValues = DetectContradiction(pair.A, pair.B);
            if (subjectAndValues is null) continue;
            var (subject, valueA, valueB) = subjectAndValues.Value;
            contradictions.Add(new ContradictionFindingDetail(
                subject, valueA, valueB, Evidence(pair.A), Evidence(pair.B), 90));
        }

        return contradictions
            .DistinctBy(item => $"{item.Subject}:{item.ValueA}:{item.ValueB}")
            .Take(50)
            .ToList();
    }

    private static (string Subject, string ValueA, string ValueB)? DetectContradiction(Statement a, Statement b)
    {
        var textA = a.Text.Trim();
        var textB = b.Text.Trim();

        var dotNetA = Regex.Match(textA, @"\.NET\s*(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
        var dotNetB = Regex.Match(textB, @"\.NET\s*(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
        if (dotNetA.Success && dotNetB.Success &&
            !dotNetA.Groups[1].Value.Equals(dotNetB.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
            return ("Runtime version", dotNetA.Value, dotNetB.Value);

        foreach (var subject in new[] { "Deployment Date", "Claims", "Approval", "Status", "Version" })
        {
            var dateA = Regex.Match(textA, $@"{subject}\s*[=:]\s*(\d{{4}}-\d{{2}}-\d{{2}})", RegexOptions.IgnoreCase);
            var dateB = Regex.Match(textB, $@"{subject}\s*[=:]\s*(\d{{4}}-\d{{2}}-\d{{2}})", RegexOptions.IgnoreCase);
            if (dateA.Success && dateB.Success && dateA.Groups[1].Value != dateB.Groups[1].Value)
                return (subject, dateA.Groups[1].Value, dateB.Groups[1].Value);
        }

        var claimsOpposing = (textA.Contains("claims require review", StringComparison.OrdinalIgnoreCase) &&
                              textB.Contains("claims auto approved", StringComparison.OrdinalIgnoreCase)) ||
                             (textB.Contains("claims require review", StringComparison.OrdinalIgnoreCase) &&
                              textA.Contains("claims auto approved", StringComparison.OrdinalIgnoreCase));
        if (claimsOpposing)
        {
            var valueA = textA.Contains("claims require review", StringComparison.OrdinalIgnoreCase)
                ? "Claims require review" : "Claims auto approved";
            var valueB = textB.Contains("claims require review", StringComparison.OrdinalIgnoreCase)
                ? "Claims require review" : "Claims auto approved";
            return ("Claims approval", valueA, valueB);
        }

        var separatorA = textA.IndexOf('=');
        var separatorB = textB.IndexOf('=');
        if (separatorA > 0 && separatorB > 0)
        {
            var subjectA = textA[..separatorA].Trim();
            var subjectB = textB[..separatorB].Trim();
            var valueA = textA[(separatorA + 1)..].Trim();
            var valueB = textB[(separatorB + 1)..].Trim();
            if (subjectA.Equals(subjectB, StringComparison.OrdinalIgnoreCase) &&
                !valueA.Equals(valueB, StringComparison.OrdinalIgnoreCase))
                return (subjectA, valueA, valueB);
        }

        return null;
    }

    private List<Finding> ScanExplicit(ParsedDocument document, string agent,
        string[] allowedLabels, string[] semanticTerms)
    {
        var result = new List<Finding>();
        foreach (var statement in document.Statements)
        {
            if (TryGetExplicitLabel(statement.Text, out var label) &&
                allowedLabels.Contains(label.Type, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(BuildFinding(statement, label.Type, agent,
                    $"Explicit {label.Type} label", label.Priority, label.Score));
                continue;
            }

            if (semanticTerms.Any(term => statement.Text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(BuildFinding(statement, "Requirement", agent,
                    "Requirement semantics detected", "High", 76));
            }
        }
        return result;
    }

    private List<Finding> ScanPatterns(ParsedDocument document, string findingType, string agent,
        string[] criticalTerms, string[] additionalTerms)
    {
        var result = new List<Finding>();
        foreach (var statement in document.Statements)
        {
            var text = statement.Text;
            var hasExplicitLabel = TryGetExplicitLabel(text, out var explicitLabel) &&
                                   explicitLabel.Type.Equals(findingType, StringComparison.OrdinalIgnoreCase);
            var criticalTerm = CriticalTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            var highTerm = HighTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
            var semanticTerm = additionalTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

            if (!hasExplicitLabel && !criticalTerm && !highTerm && !semanticTerm) continue;

            if (criticalTerm || (hasExplicitLabel && explicitLabel.Priority == "Critical"))
            {
                result.Add(BuildFinding(statement, findingType, agent,
                    "Explicit or pattern-matched critical risk", "Critical", 95));
            }
            else if (highTerm)
            {
                result.Add(BuildFinding(statement, findingType, agent,
                    "High-priority pattern detected", "High", 82));
            }
            else
            {
                result.Add(BuildFinding(statement, findingType, agent,
                    hasExplicitLabel ? $"Explicit {findingType} label" : $"{findingType} pattern detected",
                    hasExplicitLabel ? explicitLabel.Priority : "Medium",
                    hasExplicitLabel ? explicitLabel.Score : 68));
            }
        }
        return result;
    }

    private static bool TryGetExplicitLabel(string text, out (string Type, string Priority, int Score) label)
    {
        var match = Regex.Match(text.Trim(), @"^([A-Za-z]+(?:\s[A-Za-z]+)?):", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var key = match.Groups[1].Value.Trim();
            if (ExplicitLabels.TryGetValue(key, out label)) return true;
        }
        label = default;
        return false;
    }

    public Finding BuildFinding(Statement statement, string type, string agent, string title,
        string priority, int score)
    {
        var confidence = 92;
        var band = "Explicit Evidence";
        if (score < 70) { confidence = 68; band = "Inferred"; }
        else if (score < 85) { confidence = 82; band = "Strong Evidence"; }

        return new Finding
        {
            FindingType = type,
            AgentName = agent,
            Title = title,
            Description = statement.Text,
            Priority = priority,
            Score = score,
            Confidence = confidence,
            ConfidenceBand = band,
            Evidence = Evidence(statement)
        };
    }

    public static EvidenceRef Evidence(Statement statement) => new(
        statement.Id,
        statement.Evidence,
        statement.SourceLineStart,
        statement.SourceLineEnd,
        statement.Section);
}
