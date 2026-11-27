using System.Text.Json.Serialization;

namespace AI.Digest.Api.Models;

public sealed class PresentationRequest
{
    public string SourceType { get; init; } = "UseCase";
    public string SourceReference { get; init; } = "";
    public string? AnalysisContext { get; init; }
    public string? UseCase { get; init; }
    public string Title { get; init; } = "AI Presentation";
    public string TargetAudience { get; init; } = "Enterprise stakeholders";
    public string Purpose { get; init; } = "Explain the project, solution and business value";
    public string PresentationMode { get; init; } = "InteractiveStorytelling";
    public string Duration { get; init; } = "15 minutes";
    public string PresentationStyle { get; init; } = "Visual storytelling";
    public string ContentDepth { get; init; } = "Standard";
    public string Theme { get; init; } = "Prism Blue";
    public int SlideCount { get; init; } = 10;
    public string Language { get; init; } = "English";
    public string AspectRatio { get; init; } = "16:9";
    public string UserInstructions { get; init; } = "";
    public string VisualIntensity { get; init; } = "Balanced";
    public bool IncludeSpeakerNotes { get; init; } = true;
    public bool IncludeArchitectureDiagrams { get; init; } = true;
    public bool IncludeScreenshots { get; init; } = true;
    public bool IncludeFlowcharts { get; init; } = true;
    public bool IncludeDataFlowDiagrams { get; init; } = true;
    public bool IncludeCharts { get; init; } = true;
    public bool IncludeProblemSolution { get; init; } = true;
    public bool IncludeTechnicalDetails { get; init; } = true;
    public bool IncludeBusinessRecommendations { get; init; } = true;
    public bool IncludeRoadmap { get; init; } = true;
    public string? CloudConsentScope { get; init; }
    public string? CloudConsentPolicyVersion { get; init; }
    [JsonIgnore]
    public bool AllowCloudProcessing { get; set; }
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
    public string Theme { get; init; } = "Prism Blue";
    public string ThemeRecommendation { get; init; } = "";
    public string Language { get; init; } = "English";
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
    public string SectionId { get; init; } = Guid.NewGuid().ToString("N");
    public int SlideNumber { get; init; }
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string LayoutType { get; init; } = "Content";
    public List<string> Content { get; init; } = [];
    public List<PresentationVisualElement> VisualElements { get; init; } = [];
    public PresentationDiagram? Diagram { get; init; }
    public PresentationChart? Chart { get; init; }
    public PresentationTable? Table { get; init; }
    public List<string> ScreenshotReferences { get; init; } = [];
    public string SpeakerNotes { get; init; } = "";
    public List<string> SourceReferences { get; init; } = [];
    public string FontFamily { get; init; } = "Aptos";
    public string BackgroundColor { get; init; } = "";
    public string TextColor { get; init; } = "";
    public string AccentColor { get; init; } = "";
    public double ContentScale { get; init; } = 1;
    public string ContentAlignment { get; init; } = "Left";
    public double ContentSpacing { get; init; } = 14;
    public string? ImageDataUrl { get; init; }
}

public sealed class PresentationEditRequest
{
    public string Instruction { get; init; } = "";
    public string Scope { get; init; } = "SelectedSlide";
    public List<string> SectionIds { get; init; } = [];
    public string? Element { get; init; }
    public string? CloudConsentScope { get; init; }
    public string? CloudConsentPolicyVersion { get; init; }
    public PresentationDocument Presentation { get; init; } = new();
}

public sealed class PresentationEditPlan
{
    public string Summary { get; init; } = "";
    public string? Title { get; init; }
    public string? TargetAudience { get; init; }
    public string? Theme { get; init; }
    public List<PresentationSlide> Slides { get; init; } = [];
}

public sealed class PresentationEditProposal
{
    public string Summary { get; init; } = "";
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public PresentationDocument Presentation { get; init; } = new();
}

public sealed class PresentationVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Label { get; init; } = "";
    public PresentationDocument Presentation { get; init; } = new();
}

public sealed class PresentationVersionSummary
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string Label { get; init; } = "";
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

public sealed class PresentationTable
{
    public List<string> Headers { get; init; } = [];
    public List<List<string>> Rows { get; init; } = [];
}

public sealed class PresentationExportResult
{
    public Guid PresentationId { get; init; }
    public string PptxFileName { get; init; } = "";
    public string PdfFileName { get; init; } = "";
    public PresentationDocument Presentation { get; init; } = new();
}
