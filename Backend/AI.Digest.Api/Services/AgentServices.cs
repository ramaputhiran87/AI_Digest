using System.Text.RegularExpressions;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed class AgentServices
{
    public List<Finding> RequirementAgent(ParsedDocument document)
    {
        var findings = new List<Finding>();
        foreach (var statement in document.Statements)
        {
            if (Regex.IsMatch(statement.Text, @"\b(must|shall|required|approval|acceptance criteria)\b",
                RegexOptions.IgnoreCase))
            {
                findings.Add(Build(statement, "Requirement", "RequirementAgent",
                    "Requirement or approval term detected",
                    statement.Text.Contains("approval", StringComparison.OrdinalIgnoreCase) ? "High" : "Medium",
                    statement.Text.Contains("approval", StringComparison.OrdinalIgnoreCase) ? 78 : 58));
            }
        }
        return findings;
    }

    public List<Finding> SecurityAgent(ParsedDocument document)
    {
        var terms = new[] { "authentication", "authorization", "encryption", "pii", "secret", "owasp", "token", "data loss" };
        return Scan(document, "Security", "SecurityAgent", terms, ("pii|data loss|secret|breach", "Critical", 95),
            ("authentication|authorization|encryption|owasp|token", "High", 82));
    }

    public List<Finding> ComplianceAgent(ParsedDocument document) =>
        Scan(document, "Compliance", "ComplianceAgent",
            ["gdpr", "hipaa", "pci", "soc2", "governance", "regulatory"],
            ("gdpr|hipaa|pci|regulatory", "Critical", 92),
            ("soc2|governance", "High", 74));

    public List<Finding> BusinessImpactAgent(ParsedDocument document) =>
        Scan(document, "ProductionImpact", "BusinessImpactAgent",
            ["revenue", "customer", "sla", "outage", "production", "operational"],
            ("outage|production outage", "Critical", 94),
            ("revenue|customer|sla|production|operational", "High", 76));

    public List<Finding> HumanReviewAgent(ParsedDocument document)
    {
        var findings = new List<Finding>();
        foreach (var statement in document.Statements)
        {
            if (Regex.IsMatch(statement.Text, @"\b(owner|approver|due date|assumption|approval|decision)\b",
                RegexOptions.IgnoreCase))
                findings.Add(Build(statement, "HumanReview", "HumanReviewAgent",
                    "Statement requires human ownership or decision confirmation", "High", 72));
        }
        return findings;
    }

    public List<ContradictionFinding> ContradictionAgent(ParsedDocument document)
    {
        var contradictions = new List<ContradictionFinding>();
        var pairs = document.Statements
            .SelectMany((a, i) => document.Statements.Skip(i + 1).Select(b => (A: a, B: b)))
            .Where(pair =>
                (pair.A.Text.Contains("version", StringComparison.OrdinalIgnoreCase) &&
                 pair.B.Text.Contains("version", StringComparison.OrdinalIgnoreCase)) ||
                (pair.A.Text.Contains("before", StringComparison.OrdinalIgnoreCase) &&
                 pair.B.Text.Contains("after", StringComparison.OrdinalIgnoreCase)))
            .Take(20);

        foreach (var pair in pairs)
        {
            var finding = new Finding
            {
                FindingType = "Contradiction",
                AgentName = "ContradictionAgent",
                Title = "Potential statement contradiction",
                Description = "Two statements contain conflicting version or sequencing language.",
                Priority = "High",
                Score = 82,
                Confidence = 68,
                ConfidenceBand = "Inferred"
            };
            contradictions.Add(new ContradictionFinding(finding,
                Evidence(pair.A), Evidence(pair.B)));
        }
        return contradictions;
    }

    private List<Finding> Scan(ParsedDocument document, string findingType, string agent,
        IEnumerable<string> terms, (string Pattern, string Priority, int Score) critical,
        (string Pattern, string Priority, int Score) high)
    {
        var findings = new List<Finding>();
        foreach (var statement in document.Statements)
        {
            var text = statement.Text.ToLowerInvariant();
            if (!terms.Any(term => text.Contains(term))) continue;
            var (pattern, priority, score) = Regex.IsMatch(text, critical.Pattern, RegexOptions.IgnoreCase)
                ? critical
                : high;
            findings.Add(Build(statement, findingType, agent,
                $"Deterministic keyword match for {findingType.ToLowerInvariant()} terms", priority, score));
        }
        return findings;
    }

    public Finding Build(Statement statement, string type, string agent, string description,
        string priority, int score)
    {
        var confidence = 92;
        var band = "Explicit Evidence";
        if (score < 70) { confidence = 68; band = "Inferred"; }
        else if (score < 85) { confidence = 82; band = "Strong Evidence"; }
        var evidence = Evidence(statement);
        return new Finding
        {
            FindingType = type,
            AgentName = agent,
            Title = description,
            Description = statement.Text,
            Priority = priority,
            Score = score,
            Confidence = confidence,
            ConfidenceBand = band,
            Evidence = evidence
        };
    }

    public static EvidenceRef Evidence(Statement statement) => new(
        statement.Id,
        statement.Evidence,
        statement.SourceLineStart,
        statement.SourceLineEnd,
        statement.Section);
}
