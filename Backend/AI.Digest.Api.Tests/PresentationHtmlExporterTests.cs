using AI.Digest.Api.Models;
using AI.Digest.Api.Services;
using Xunit;

namespace AI.Digest.Api.Tests;

public sealed class PresentationHtmlExporterTests
{
    [Fact]
    public void ExportIsStandaloneAndEncodesPresentationTextAsJson()
    {
        var presentation = new PresentationDocument
        {
            Title = "<script>alert(1)</script>",
            SourceReference = "https://private.example/repository",
            AIProvider = "Gemini",
            Slides =
            [
                new PresentationSlide
                {
                    SlideNumber = 1,
                    Title = "<img src=x onerror=alert(1)>",
                    Content = ["Verified point"],
                    SpeakerNotes = "Presenter detail",
                    Diagram = new PresentationDiagram
                    {
                        Type = "Workflow",
                        Mermaid = "flowchart TD\n A[Upload] --> B[Review]"
                    }
                }
            ]
        };

        var html = PresentationHtmlExporter.Export(presentation);

        Assert.Contains("application/json", html);
        Assert.Contains("\\u003Cscript\\u003E", html);
        Assert.Contains("\\u003Cimg", html);
        Assert.Contains("Presenter detail", html);
        Assert.Contains("requestFullscreen", html);
        Assert.Contains("connect-src 'none'", html);
        Assert.DoesNotContain("private.example", html);
        Assert.DoesNotContain("AIProvider", html);
        Assert.DoesNotContain("https://", html);
        Assert.DoesNotContain("<script src=", html);
    }

    [Fact]
    public void ExportFilenameRemovesUnsafeCharacters()
    {
        Assert.Equal("Client-Review.html", PresentationHtmlExporter.SafeFileName("Client Review"));
        Assert.Equal("interactive-presentation.html", PresentationHtmlExporter.SafeFileName("  "));
    }

    [Fact]
    public void ExportsSelectedPremiumThemeAndSourceReferencesOffline()
    {
        var presentation = new PresentationDocument
        {
            Title = "Repository story",
            Theme = "Digital Blueprint",
            Slides =
            [
                new PresentationSlide
                {
                    SlideNumber = 1,
                    LayoutType = "ArchitectureDiagram",
                    Title = "System map",
                    SourceReferences = ["README.md · Architecture"]
                }
            ]
        };

        var html = PresentationHtmlExporter.Export(presentation);

        Assert.Contains("theme-blueprint", html);
        Assert.Contains("ArchitectureDiagram", html);
        Assert.Contains("sourceReferences", html);
        Assert.Contains("connect-src 'none'", html);
    }

    [Fact]
    public void PrismBlueIsTheDefaultAndIsRenderedWithoutExternalAssets()
    {
        var presentation = new PresentationDocument
        {
            Language = "Tamil",
            Slides =
            [
                new PresentationSlide
                {
                    SlideNumber = 1,
                    Title = "தமிழ் மற்றும் enterprise",
                    Content = ["Source-grounded content"]
                }
            ]
        };

        var html = PresentationHtmlExporter.Export(presentation);

        Assert.Contains("\"themeKey\":\"prism\"", html);
        Assert.Contains("<html lang=\"ta\">", html);
        Assert.Contains("body.theme-prism", html);
        Assert.Contains("Noto Sans Tamil", html);
        Assert.Contains("connect-src 'none'", html);
        Assert.DoesNotContain("__PRISM_STYLES__", html);
        Assert.DoesNotContain("__DOCUMENT_LANGUAGE__", html);
        Assert.DoesNotContain("<script src=", html);
    }

    [Fact]
    public void OfflineRendererPreservesBranchingDiagramAndChartSemantics()
    {
        var presentation = new PresentationDocument
        {
            Slides =
            [
                new PresentationSlide
                {
                    SlideNumber = 1,
                    Title = "Decision flow",
                    LayoutType = "DecisionTree",
                    Diagram = new PresentationDiagram
                    {
                        Type = "Workflow",
                        Mermaid = "flowchart TD\n A[Start] --> B{Decision}\n B -->|Yes| C[Approve]\n B -->|No| D[Review]"
                    },
                    Chart = new PresentationChart
                    {
                        Type = "Donut",
                        Labels = ["Approved", "Review"],
                        Values = [7, 3]
                    }
                }
            ]
        };

        var html = PresentationHtmlExporter.Export(presentation);

        Assert.Contains("diagram-edge-label", html);
        Assert.Contains("createElementNS(svgNs,\"polygon\")", html);
        Assert.Contains("flowchart TD", html);
        Assert.Contains("type.includes(\"donut\")", html);
        Assert.Contains("stroke-dasharray", html);
        Assert.Contains("diagram-controls", html);
        Assert.DoesNotContain("labels.forEach((label,i)", html);
    }

    [Fact]
    public void OfflineRendererIncludesPresentationTables()
    {
        var presentation = new PresentationDocument
        {
            Slides =
            [
                new PresentationSlide
                {
                    SlideNumber = 1,
                    Title = "Delivery plan",
                    Table = new PresentationTable
                    {
                        Headers = ["Phase", "Owner"],
                        Rows = [["Plan", "Team A"]]
                    }
                }
            ]
        };

        var html = PresentationHtmlExporter.Export(presentation);

        Assert.Contains("function renderTable(parent,table)", html);
        Assert.Contains("table.rows.slice(0,20)", html);
        Assert.Contains("Team A", html);
    }

    [Theory]
    [InlineData("Neural Intelligence", "\"themeKey\":\"neural\"")]
    [InlineData("Cinematic Product Launch", "\"themeKey\":\"cinematic\"")]
    [InlineData("Executive Gold", "\"themeKey\":\"gold\"")]
    [InlineData("Digital Blueprint", "\"themeKey\":\"blueprint\"")]
    [InlineData("Immersive Storytelling", "\"themeKey\":\"story\"")]
    [InlineData("Knowledge Universe", "\"themeKey\":\"universe\"")]
    public void MapsSupportedPremiumThemesToOfflineRenderingPalettes(
        string theme,
        string expectedThemeKey)
    {
        var html = PresentationHtmlExporter.Export(
            new PresentationDocument { Theme = theme });

        Assert.Contains(expectedThemeKey, html);
    }
}
