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
        var opportunities = agents.DreamAgent(document);
        var hiddenRisks = agents.DarkMatterAgent(document);
        var futureRisks = agents.TimeMachineAgent(document);
        var rulesRecommendations = agents.RecommendationAgent(document);
        var criticalRiskFindings = agents.CriticalRiskAgent(document);
        var highPriorityFindings = agents.HighPriorityAgent(document);

        var all = requirement
            .Concat(security)
            .Concat(compliance)
            .Concat(impact)
            .Concat(human)
            .Concat(opportunities)
            .Concat(hiddenRisks)
            .Concat(futureRisks)
            .Concat(rulesRecommendations)
            .Concat(criticalRiskFindings)
            .Concat(highPriorityFindings)
            .ToList();
        all = all.Where(f => f.Evidence is not null)
            .GroupBy(f => $"{f.FindingType}:{f.AgentName}:{f.Evidence?.SourceText}")
            .Select(g => g.OrderByDescending(f => f.Score).First()).ToList();
        all.Sort((a, b) => b.Score.CompareTo(a.Score));

        var evidence = all.Where(f => f.Evidence is not null)
            .Select(f => f.Evidence!)
            .DistinctBy(e => $"{e.SourceLineStart}:{e.SourceText}")
            .ToList();
        var recommendations = all
            .Where(f => f.FindingType == "Recommendation")
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

        var evidenceQuality = evidence.Count == 0 ? 0 : 100;
        var consistency = contradictionDetails.Count == 0 ? 100 :
            Math.Max(60, 100 - contradictionDetails.Count * 10);
        var securityScore = security.Count == 0 ? 100 :
            (int)Math.Round(security.Average(f => f.Confidence));
        var complianceScore = compliance.Count == 0 ? 100 :
            (int)Math.Round(compliance.Average(f => f.Confidence));
        var darkMatterScore = hiddenRisks.Count == 0 ? 100 :
            (int)Math.Round(hiddenRisks.Average(f => f.Confidence));
        var timeMachineScore = futureRisks.Count == 0 ? 100 :
            (int)Math.Round(futureRisks.Average(f => f.Confidence));
        var confidence = (int)Math.Round(
            evidenceQuality * 0.30 +
            securityScore * 0.20 +
            complianceScore * 0.15 +
            consistency * 0.15 +
            darkMatterScore * 0.10 +
            timeMachineScore * 0.10);
        confidence = evidence.Count == 0 ? 0 : Math.Clamp(confidence, 0, 100);
        var compressed = BuildCompressedDocument(document, all, contradictionFindings, human,
            recommendations, hiddenRisks, futureRisks, opportunities);
        var highPriority = all.Where(f => f.Priority == "High").ToList();
        var criticalRisks = all.Where(f => f.Priority == "Critical").ToList();
        var dependencies = all.Where(f => f.FindingType == "Dependency").ToList();
        var actionItems = all.Where(f => f.FindingType == "ActionItem").ToList();
        var result = new DigestResult
        {
            DocumentTitle = document.Title,
            DocumentType = document.SourceType,
            ExecutiveSummary = evidence.Count == 0
                ? "No evidence-backed findings were identified. Human review is required."
                : $"Analysis identified {all.Count} evidence-backed findings, {contradictionDetails.Count} contradictions, {human.Count} human-review items, and {opportunities.Count} opportunities across {all.Select(f => f.AgentName).Distinct().Count()} specialist agents.",
            HighPriorityItems = highPriority.Take(50).ToList(),
            CriticalRisks = criticalRisks.Take(50).ToList(),
            Dependencies = dependencies.Take(50).ToList(),
            Contradictions = contradictionFindings,
            ContradictionDetails = contradictionDetails,
            HumanReviewRequired = human.Take(20).ToList(),
            Recommendations = recommendations,
            KeyFindings = all.Take(50).ToList(),
            HiddenRisks = hiddenRisks.Take(20).ToList(),
            FutureRisks = futureRisks.Take(20).ToList(),
            StrategicOpportunities = opportunities.Take(20).ToList(),
            ActionItems = actionItems.Take(20).ToList(),
            ConfidenceScore = confidence,
            ConfidenceBand = confidence >= 90 ? "Visionary Confidence" : confidence >= 75 ? "Strong Evidence" :
                confidence >= 50 ? "Moderate Confidence" : "Weak Evidence",
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
        IReadOnlyCollection<Finding> recommendations,
        IReadOnlyCollection<Finding> hiddenRisks,
        IReadOnlyCollection<Finding> futureRisks,
        IReadOnlyCollection<Finding> opportunities)
    {
        var originalLines = document.Metadata.TryGetValue("LineCount", out var count)
            ? int.Parse(count)
            : document.Statements.Count;

        var retained = new[] { "Decision", "Requirement", "Risk", "Security", "Compliance",
            "Dependency", "Approval", "Blocker", "ActionItem", "Contradiction",
            "ProductionImpact", "HumanReview", "Recommendation", "Opportunity", "HiddenRisk", "FutureRisk" };

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
            "",
            "## Executive Summary",
            $"Analysis retained {findings.Count} evidence-backed findings from {document.Title}.",
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
        markdown.AddRange(FindingSection("Hidden Risks", hiddenRisks));
        markdown.Add("");
        markdown.AddRange(FindingSection("Future Failure Predictions", futureRisks));
        markdown.Add("");
        markdown.AddRange(FindingSection("AI Opportunities", opportunities));
        markdown.Add("");
        markdown.AddRange(FindingSection("Action Items",
            findings.Where(item => item.FindingType == "ActionItem")));
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
        var headingLevel = 1;
        var sectionPath = new List<string>();
        var noiseTags = new[] { "script", "style", "nav", "footer", "header" };

        for (var i = 0; i < lines.Length; i++)
        {
            var rawLine = lines[i];
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (extension == ".json")
            {
                var properties = ExtractJsonProperties(rawLine);
                if (properties.Count > 0)
                {
                    statements.AddRange(properties.Select(property =>
                        new Statement(Guid.NewGuid(), property, currentHeading, i + 1, i + 1, rawLine.Trim())));
                    continue;
                }
            }
            else if (extension == ".xml")
            {
                var elements = ExtractXmlElements(rawLine);
                if (elements.Count > 0)
                {
                    statements.AddRange(elements.Select(element =>
                        new Statement(Guid.NewGuid(), element, currentHeading, i + 1, i + 1, rawLine.Trim())));
                    continue;
                }
            }

            if (extension is ".html" or ".htm")
            {
                var lower = line.ToLowerInvariant();
                if (noiseTags.Any(tag => lower.Contains($"<{tag}") || lower.Contains($"</{tag}>")))
                    continue;
                line = StripHtmlLine(line);
                if (string.IsNullOrWhiteSpace(line)) continue;
            }
            var markdownHeading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            var explicitLabel = Regex.IsMatch(line, @"^(?:[-*+]|\d+[.)])?\s*(?:\[[ xX]\]\s*)?[A-Za-z]+(?:\s[A-Za-z]+)?:");
            if (markdownHeading.Success || (!explicitLabel && line.EndsWith(':')))
            {
                if (i + 1 > sectionStart && i > 0)
                    sections.Add(new DocumentSection(++headingIndex, currentHeading, sectionStart, i, "", headingLevel));
                headingLevel = markdownHeading.Success ? markdownHeading.Groups[1].Length : 1;
                var heading = markdownHeading.Success
                    ? markdownHeading.Groups[2].Value.Trim()
                    : line.TrimEnd(':');
                if (sectionPath.Count > headingLevel - 1)
                    sectionPath.RemoveRange(headingLevel - 1, sectionPath.Count - (headingLevel - 1));
                sectionPath.Add(heading);
                currentHeading = string.Join(" > ", sectionPath);
                sectionStart = i + 1;
            }
            else
            {
                statements.Add(new Statement(Guid.NewGuid(), line, currentHeading, i + 1, i + 1, rawLine.Trim()));
            }
        }
        sections.Add(new DocumentSection(++headingIndex, currentHeading, sectionStart, lines.Length, "", headingLevel));
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

    private static List<string> ExtractXmlElements(string line)
    {
        return Regex.Matches(line,
                @"<(?<name>[A-Za-z_][\w:.-]*)(?:\s+[^>]*)?>(?<value>[^<]+)</\k<name>>")
            .Select(match => $"{match.Groups["name"].Value}: {match.Groups["value"].Value.Trim()}")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
    }

    private static List<string> ExtractJsonProperties(string line)
    {
        return Regex.Matches(line,
                @"""(?<name>[^""]+)""\s*:\s*(?<value>""(?:\\.|[^""])*""|[^,}]+)")
            .Select(match =>
            {
                var value = match.Groups["value"].Value.Trim().TrimEnd(',');
                if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                    value = value[1..^1];
                return $"{match.Groups["name"].Value}: {value}";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
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
