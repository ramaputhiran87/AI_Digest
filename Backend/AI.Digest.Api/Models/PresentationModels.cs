namespace AI.Digest.Api.Models;

public sealed class PresentationRequest
{
    public string SourceType { get; init; } = "UseCase";
    public string SourceReference { get; init; } = "";
    public string? UseCase { get; init; }
    public string Title { get; init; } = "AI Presentation";
    public string TargetAudience { get; init; } = "Enterprise stakeholders";
    public string Purpose { get; init; } = "Explain the project, solution and business value";
    public int SlideCount { get; init; } = 10;
    public string Language { get; init; } = "English";
    public string AspectRatio { get; init; } = "16:9";
    public bool IncludeSpeakerNotes { get; init; } = true;
    public bool IncludeArchitectureDiagrams { get; init; } = true;
    public bool IncludeScreenshots { get; init; } = true;
}

public sealed class PresentationSource
{
    public string SourceType { get; init; } = "";
    public string SourceReference { get; init; } = "";
    public string Text { get; init; } = "";
    public List<string> SourceFiles { get; init; } = [];
}

public sealed class PresentationDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string SourceType { get; init; } = "";
    public string SourceReference { get; init; } = "";
    public string TargetAudience { get; init; } = "";
    public string Theme { get; init; } = "AI_Digest Navy Blue";
    public string AspectRatio { get; init; } = "16:9";
    public int TotalSlides { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string AIProvider { get; init; } = "";
    public string AIModel { get; init; } = "";
    public string Status { get; init; } = "Completed";
    public List<PresentationSlide> Slides { get; init; } = [];
}

public sealed class PresentationSlide
{
    public int SlideNumber { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string LayoutType { get; init; } = "Content";
    public List<string> Content { get; init; } = [];
    public List<PresentationVisualElement> VisualElements { get; init; } = [];
    public PresentationDiagram? Diagram { get; init; }
    public PresentationChart? Chart { get; init; }
    public List<string> ScreenshotReferences { get; init; } = [];
    public string SpeakerNotes { get; init; } = "";
    public List<string> SourceReferences { get; init; } = [];
}

public sealed class PresentationVisualElement
{
    public string Type { get; init; } = "Card";
    public string Label { get; init; } = "";
    public string Value { get; init; } = "";
}

public sealed class PresentationDiagram
{
    public string Type { get; init; } = "Workflow";
    public string Mermaid { get; init; } = "";
}

public sealed class PresentationChart
{
    public string Type { get; init; } = "Bar";
    public List<string> Labels { get; init; } = [];
    public List<double> Values { get; init; } = [];
}

public sealed class PresentationExportResult
{
    public Guid PresentationId { get; init; }
    public string PptxFileName { get; init; } = "";
    public string PdfFileName { get; init; } = "";
    public PresentationDocument Presentation { get; init; } = new();
}
