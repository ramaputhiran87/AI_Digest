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
        var contradictionDetails = agents.ContradictionAgent(document);

        var all = requirement.Concat(security).Concat(compliance).Concat(impact).Concat(human).ToList();
        all = all.GroupBy(f => $"{f.FindingType}:{f.Evidence?.SourceLineStart}:{f.Evidence?.SourceText}")
            .Select(g => g.OrderByDescending(f => f.Score).First()).ToList();
        all.Sort((a, b) => b.Score.CompareTo(a.Score));

        var evidence = all.Where(f => f.Evidence is not null)
            .Select(f => f.Evidence!)
            .DistinctBy(e => $"{e.SourceLineStart}:{e.SourceText}")
            .ToList();
        var recommendations = all
            .Where(f => f.FindingType is "Recommendation" or "Requirement")
            .Take(20).ToList();

        var contradictionFindings = contradictionDetails.Select(detail =>
            new ContradictionFinding(
                new Finding
                {
                    FindingType = "Contradiction",
                    AgentName = "ContradictionAgent",
                    Title = $"{detail.Subject}: {detail.ValueA} vs {detail.ValueB}",
                    Description = $"Conflicting values detected for {detail.Subject}.",
                    Priority = "High",
                    Score = 82,
                    Confidence = detail.Confidence,
                    ConfidenceBand = "Strong Evidence",
                    Evidence = detail.EvidenceA
                },
                detail.EvidenceA,
                detail.EvidenceB)).ToList();

        var explicitLabels = all.Count(f =>
            f.Title.StartsWith("Explicit", StringComparison.OrdinalIgnoreCase));
        var completeness = document.Statements.Count == 0 ? 0 :
            Math.Min(100, 70 + Math.Min(30, document.Statements.Count));
        var evidenceQuality = all.Count == 0 ? 0 :
            (explicitLabels == 0 ? 75 : 100);
        var consistency = contradictionDetails.Count == 0 ? 100 :
            Math.Max(60, 100 - contradictionDetails.Count * 10);
        var classificationCertainty = all.Count == 0 ? 0 :
            (int)Math.Round(all.Average(f => f.Confidence));
        var confidence = (int)Math.Round(
            completeness * 0.35 +
            evidenceQuality * 0.25 +
            consistency * 0.20 +
            classificationCertainty * 0.20);
        confidence = evidence.Count == 0 ? 0 : Math.Clamp(confidence, 0, 100);
        var compressed = BuildCompressedDocument(document, all, contradictionFindings, human, recommendations);
        var result = new DigestResult
        {
            DocumentTitle = document.Title,
            DocumentType = document.SourceType,
            ExecutiveSummary = evidence.Count == 0
                ? "No evidence-backed findings were identified. Human review is required."
                : $"Analysis identified {all.Count} evidence-backed findings, {contradictionDetails.Count} contradictions, and {human.Count} human-review items across {all.Select(f => f.AgentName).Distinct().Count()} specialist agents.",
            HighPriorityItems = all.Where(f => f.Priority == "High").Take(20).ToList(),
            CriticalRisks = all.Where(f => f.Priority == "Critical").Take(20).ToList(),
            Dependencies = all.Where(f => f.FindingType is "Dependency" or "Requirement").Take(20).ToList(),
            Contradictions = contradictionFindings,
            ContradictionDetails = contradictionDetails,
            HumanReviewRequired = human.Take(20).ToList(),
            Recommendations = recommendations,
            ConfidenceScore = confidence,
            ConfidenceBand = confidence >= 90 ? "Explicit Evidence" : confidence >= 75 ? "Strong Evidence" :
                confidence >= 50 ? "Inferred" : "Weak Evidence",
            Evidence = evidence,
            CompressedDocument = compressed
        };
        return Task.FromResult(result);
    }

    private static CompressedDocument BuildCompressedDocument(
        ParsedDocument document,
        IReadOnlyCollection<Finding> findings,
        IReadOnlyCollection<ContradictionFinding> contradictions,
        IReadOnlyCollection<Finding> humanReview,
        IReadOnlyCollection<Finding> recommendations)
    {
        var originalLines = document.Metadata.TryGetValue("LineCount", out var count)
            ? int.Parse(count)
            : document.Statements.Count;

        var retained = new[] { "Decision", "Requirement", "Risk", "Security", "Compliance",
            "Dependency", "Approval", "Blocker", "ActionItem", "Contradiction",
            "ProductionImpact", "HumanReview", "Recommendation" };

        List<string> FindingSection(string title, IEnumerable<Finding> items)
        {
            var lines = new List<string> { $"## {title}" };
            foreach (var item in items.Where(item => retained.Contains(item.FindingType)))
            {
                var evidence = item.Evidence;
                var source = evidence is null
                    ? ""
                    : $" — source: {evidence.Section}, line {evidence.SourceLineStart}-{evidence.SourceLineEnd}";
                lines.Add($"- **{item.Priority}** — {item.Description}{source}");
            }
            if (lines.Count == 1) lines.Add("- None identified.");
            return lines;
        }

        var markdown = new List<string>
        {
            "# Compressed Review",
            ""
        };
        markdown.AddRange(FindingSection("Critical Risks",
            findings.Where(item => item.Priority == "Critical")));
        markdown.Add("");
        markdown.AddRange(FindingSection("High Priority",
            findings.Where(item => item.Priority == "High")));
        markdown.Add("");
        markdown.AddRange(FindingSection("Dependencies",
            findings.Where(item => item.FindingType is "Dependency" or "Requirement")));
        markdown.Add("");
        markdown.AddRange(FindingSection("Human Review", humanReview));
        markdown.Add("");
        markdown.Add("## Contradictions");
        if (contradictions.Count == 0)
        {
            markdown.Add("- None identified.");
        }
        else
        {
            foreach (var contradiction in contradictions)
            {
                markdown.Add($"- **High** — {contradiction.Finding.Description}");
                markdown.Add($"  - Evidence A: {contradiction.EvidenceA.SourceText} (line {contradiction.EvidenceA.SourceLineStart})");
                markdown.Add($"  - Evidence B: {contradiction.EvidenceB.SourceText} (line {contradiction.EvidenceB.SourceLineStart})");
            }
        }
        markdown.Add("");
        markdown.AddRange(FindingSection("Recommendations", recommendations));

        var markdownText = string.Join("\n", markdown);
        var compressedLines = markdown.Count(line => !string.IsNullOrWhiteSpace(line));
        var removed = Math.Max(0, originalLines - compressedLines);
        var compressionPercent = originalLines == 0 ? 0 :
            (int)Math.Round(removed * 100d / originalLines);
        var noiseRemovedPercent = Math.Clamp(compressionPercent, 0, 100);
        var fileName = Path.GetFileNameWithoutExtension(document.Title) + "_Compressed.md";

        return new CompressedDocument
        {
            FileName = fileName,
            Markdown = markdownText,
            Statistics = new CompressionStatistics
            {
                OriginalLines = originalLines,
                CompressedLines = compressedLines,
                RemovedLines = removed,
                CompressionPercent = compressionPercent,
                NoiseRemovedPercent = noiseRemovedPercent
            }
        };
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
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sections = new List<DocumentSection>();
        var statements = new List<Statement>();
        var currentHeading = "Document";
        var sectionStart = 1;
        var headingIndex = 0;
        var noiseTags = new[] { "script", "style", "nav", "footer", "header" };

        for (var i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (extension is ".html" or ".htm")
            {
                var lower = line.ToLowerInvariant();
                if (noiseTags.Any(tag => lower.Contains($"<{tag}") || lower.Contains($"</{tag}>")))
                    continue;
                line = StripHtmlLine(line);
                if (string.IsNullOrWhiteSpace(line)) continue;
            }

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

    private static string StripHtmlLine(string html)
    {
        var parser = new HtmlParser();
        var fragment = parser.ParseFragment(html, null!);
        var text = string.Join(" ", fragment.Select(node => node.TextContent)).Trim();
        text = Regex.Replace(text, @"\s+", " ");
        return text;
    }

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
