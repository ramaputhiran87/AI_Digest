using System.Text.RegularExpressions;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed class AgentServices
{
    private static readonly string[] CriticalTerms =
    [
        "password", "credentials", "secret", "apikey", "api key", "token", "pii", "ssn",
        "gdpr", "pci", "hipaa", "data breach", "breach", "vulnerability", "unencrypted", "sha1",
        "outage", "service disruption", "data loss", "ransomware", "critical", "security approval missing",
        "security failure"
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
        "business decision required", "executive approval required", "pending executive approval",
        "human review"
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
            ["Security"] = ("Security", "High", 82),
            ["Compliance"] = ("Compliance", "High", 82),
            ["Requirement"] = ("Requirement", "High", 78),
            ["Recommendation"] = ("Recommendation", "Low", 30),
            ["ProductionImpact"] = ("ProductionImpact", "High", 78),
            ["Human Review"] = ("HumanReview", "High", 72)
        };

    public List<Finding> RequirementAgent(ParsedDocument document) =>
        ScanExplicit(document, "RequirementAgent",
            new[] { "Requirement", "Decision", "Approval", "Action", "Dependency", "Blocker" },
            new[] { "must", "shall", "required", "requirement", "acceptance criteria", "ambiguous", "missing requirement", "incomplete", "requirement agent" });

    public List<Finding> SecurityAgent(ParsedDocument document) =>
        ScanPatterns(document, "Security", "SecurityAgent", CriticalTerms,
            new[] { "authentication", "authorization", "encryption", "owasp", "attack", "secret", "password", "credentials", "malware", "audit logging", "api security" });

    public List<Finding> ComplianceAgent(ParsedDocument document) =>
        ScanPatterns(document, "Compliance", "ComplianceAgent",
            new[] { "gdpr", "hipaa", "pci", "regulatory" },
            new[] { "soc2", "governance", "compliance", "compliance gap" });

    public List<Finding> BusinessImpactAgent(ParsedDocument document) =>
        ScanPatterns(document, "ProductionImpact", "BusinessImpactAgent",
            new[] { "outage", "service disruption", "data loss" },
            new[] { "revenue", "customer impact", "sla", "production", "operational impact", "deployment", "business impact" });

    public List<Finding> DreamAgent(ParsedDocument document) =>
        CreateDerivedFindings(document, "Opportunity", "A process improvement opportunity is evidenced by manual or repetitive work.",
            "DreamAgent", "Low", 55,
            ["manual", "repetitive", "paper-based", "spreadsheet-based", "time-consuming", "automate"]);

    public List<Finding> DarkMatterAgent(ParsedDocument document)
    {
        var terms = new (string Name, string[] Controls)[]
        {
            ("Monitoring", ["monitoring", "observability", "alerting"]),
            ("Disaster recovery", ["disaster recovery", "recovery plan", "dr plan"]),
            ("Rollback", ["rollback", "roll back"]),
            ("Ownership", ["owner", "ownership", "accountable"])
        };
        var allText = string.Join(" ", document.Statements.Select(statement => statement.Text));
        var scope = document.Statements.FirstOrDefault(statement =>
            Regex.IsMatch(statement.Text, @"\b(system|service|application|platform|production|deployment|project)\b",
                RegexOptions.IgnoreCase));
        if (scope is null) return [];

        var findings = new List<Finding>();
        foreach (var (name, controls) in terms)
        {
            if (controls.Any(control => allText.Contains(control, StringComparison.OrdinalIgnoreCase)))
                continue;
            findings.Add(BuildDerivedFinding(scope, "HiddenRisk", "DarkMatterAgent",
                $"{name} control is not documented in the supplied document.",
                "Medium", 55, "Inferred"));
        }
        return findings;
    }

    public List<Finding> TimeMachineAgent(ParsedDocument document) =>
        CreateDerivedFindings(document, "FutureRisk",
            "Future operational failure may occur if the documented issue remains unresolved.",
            "TimeMachineAgent", "High", 74,
            ["missing", "not defined", "unresolved", "manual", "outage", "blocker", "under discussion"]);

    public List<Finding> RecommendationAgent(ParsedDocument document) =>
        CreateDerivedFindings(document, "Recommendation",
            "Consider resolving this documented gap before approval or deployment.",
            "SupervisorAgent", "Medium", 62,
            ["missing", "ambiguous", "incomplete", "not defined", "unresolved"]);

    public List<Finding> CriticalRiskAgent(ParsedDocument document) =>
        document.Statements
            .Where(statement => CriticalTerms.Any(term =>
                statement.Text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(statement => BuildFinding(statement, "Risk", "RiskAgent",
                "Critical-risk indicator found in source evidence.", "Critical", 95))
            .ToList();

    public List<Finding> HighPriorityAgent(ParsedDocument document)
    {
        var findings = new List<Finding>();
        foreach (var statement in document.Statements)
        {
            var text = statement.Text;
            var highTerm = HighTerms.FirstOrDefault(term =>
                text.Contains(term, StringComparison.OrdinalIgnoreCase));
            var hasExplicitHighPriority = TryGetExplicitLabel(text, out var label) &&
                                          label.Priority == "High";
            if (highTerm is null && !hasExplicitHighPriority) continue;

            var findingType = highTerm switch
            {
                "missing dependency" => "Dependency",
                "security finding" => "Security",
                "compliance issue" => "Compliance",
                _ => hasExplicitHighPriority ? label.Type : "ActionItem"
            };
            findings.Add(BuildFinding(statement, findingType, "PriorityAgent",
                highTerm is null ? $"Explicit {findingType} priority label" : $"High-priority indicator: {highTerm}",
                "High", 82));
        }
        return findings;
    }

    private List<Finding> CreateDerivedFindings(ParsedDocument document, string type,
        string title, string agent, string priority, int score, string[] phrases)
    {
        return document.Statements
            .Where(statement => phrases.Any(phrase =>
                statement.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
            .Select(statement => BuildDerivedFinding(statement, type, agent, title, priority, score,
                score < 70 ? "Inferred" : "Strong Evidence"))
            .ToList();
    }

    private Finding BuildDerivedFinding(Statement statement, string type, string agent,
        string title, string priority, int score, string confidenceBand)
    {
        var finding = BuildFinding(statement, type, agent, title, priority, score);
        return new Finding
        {
            Id = finding.Id,
            FindingType = finding.FindingType,
            AgentName = finding.AgentName,
            Title = finding.Title,
            Description = finding.Description,
            Priority = finding.Priority,
            Score = finding.Score,
            Confidence = confidenceBand == "Inferred" ? 60 : finding.Confidence,
            ConfidenceBand = confidenceBand,
            Evidence = finding.Evidence
        };
    }

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
            var criticalTerm = criticalTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
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
        var candidate = Regex.Replace(text.Trim(), @"^(?:[-*+]|\d+[.)])\s+(?:\[[ xX]\]\s+)?", "");
        var match = Regex.Match(candidate, @"^([A-Za-z]+(?:\s[A-Za-z]+)?):", RegexOptions.IgnoreCase);
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
