using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AngleSharp.Html.Parser;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed class DocumentAnalysisService(AgentServices agents)
{
    public Task<DigestResult> AnalyseAsync(string title, string content, string? useCase)
    {
        var document = Parse(title, content);
        var requirement = agents.RequirementAgent(document);
        var security = agents.SecurityAgent(document);
        var compliance = agents.ComplianceAgent(document);
        var impact = agents.BusinessImpactAgent(document);
        var human = agents.HumanReviewAgent(document);
        var contradictions = agents.ContradictionAgent(document);

        var all = requirement.Concat(security).Concat(compliance).Concat(impact).Concat(human).ToList();
        all = all.GroupBy(f => $"{f.FindingType}:{f.Evidence?.SourceText}")
            .Select(g => g.OrderByDescending(f => f.Score).First()).ToList();
        all.Sort((a, b) => b.Score.CompareTo(a.Score));

        var evidence = all.Where(f => f.Evidence is not null).Select(f => f.Evidence!).DistinctBy(e => e.SourceLineStart).ToList();
        var recommendations = all.Where(f => f.FindingType is "Recommendation" or "Requirement").Take(8).ToList();
        if (!string.IsNullOrWhiteSpace(useCase))
            recommendations.Add(new Finding
            {
                FindingType = "Recommendation",
                AgentName = "DigesterSupervisor",
                Title = "Use-case context",
                Description = useCase,
                Priority = "Medium",
                Score = 55,
                Confidence = 60,
                ConfidenceBand = "Inferred"
            });

        var confidence = evidence.Count == 0 ? 45 : Math.Clamp(
            (int)evidence.Count switch
            {
                1 => 58,
                <= 5 => 76,
                _ => 92
            }, 0, 100);
        var result = new DigestResult
        {
            DocumentTitle = document.Title,
            DocumentType = document.SourceType,
            ExecutiveSummary = evidence.Count == 0
                ? "No evidence-backed findings were identified. Human review is required."
                : $"Analysis identified {all.Count} evidence-backed findings across {all.Select(f => f.AgentName).Distinct().Count()} specialist agents.",
            HighPriorityItems = all.Where(f => f.Priority == "High").Take(20).ToList(),
            CriticalRisks = all.Where(f => f.Priority == "Critical").Take(20).ToList(),
            Dependencies = all.Where(f => f.FindingType is "Dependency" or "Requirement").Take(20).ToList(),
            Contradictions = contradictions,
            HumanReviewRequired = human.Take(20).ToList(),
            Recommendations = recommendations,
            ConfidenceScore = confidence,
            ConfidenceBand = confidence >= 90 ? "Explicit Evidence" : confidence >= 75 ? "Strong Evidence" :
                confidence >= 50 ? "Inferred" : "Weak Evidence",
            Evidence = evidence
        };
        return Task.FromResult(result);
    }

    public ParsedDocument Parse(string title, string content)
    {
        var extension = Path.GetExtension(title).ToLowerInvariant();
        var type = extension switch
        {
            ".md" => "Markdown",
            ".txt" => "Plain Text",
            ".html" or ".htm" => "HTML",
            ".json" => "JSON",
            ".xml" => "XML",
            _ => "Plain Text"
        };
        var normalised = extension switch
        {
            ".html" or ".htm" => StripHtml(content),
            ".json" => FormatJson(content),
            ".xml" => StripXml(content),
            _ => content
        };
        var lines = normalised.Replace("\r\n", "\n").Split('\n');
        var sections = new List<DocumentSection>();
        var statements = new List<Statement>();
        var currentHeading = "Document";
        var sectionStart = 1;
        var headingIndex = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (Regex.IsMatch(line, @"^#{1,6}\s+") || (!line.StartsWith(' ') && line.EndsWith(':')))
            {
                if (i + 1 > sectionStart && i > 0)
                    sections.Add(new DocumentSection(++headingIndex, currentHeading, sectionStart, i, ""));
                currentHeading = Regex.Replace(line, @"^#{1,6}\s+", "").TrimEnd(':');
                sectionStart = i + 1;
            }
            else
            {
                statements.Add(new Statement(Guid.NewGuid(), line, currentHeading, i + 1, i + 1, line));
            }
        }
        sections.Add(new DocumentSection(++headingIndex, currentHeading, sectionStart, lines.Length, ""));
        return new ParsedDocument
        {
            Title = title,
            SourceType = type,
            Sections = sections,
            Statements = statements,
            Metadata = new Dictionary<string, string>
            {
                ["LineCount"] = lines.Length.ToString(),
                ["UseDeterministicAnalysis"] = "true"
            }
        };
    }

    private static string StripHtml(string html) =>
        new HtmlParser().ParseDocument(html).Body?.TextContent.Trim() ?? "";

    private static string FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch { return json; }
    }

    private static string StripXml(string xml)
    {
        try
        {
            return string.Join("\n", XDocument.Parse(xml).Descendants()
                .SelectMany(element => element.Nodes().OfType<System.Xml.Linq.XText>())
                .Select(text => text.Value.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text)));
        }
        catch { return xml; }
    }
}
