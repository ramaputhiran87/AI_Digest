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
