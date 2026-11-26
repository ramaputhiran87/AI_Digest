using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed class PresentationPlanningService(
    IConfiguration configuration,
    AiInferenceRouter inferenceRouter,
    AiProviderConfiguration providerConfiguration)
{
    public async Task<PresentationDocument> PlanAsync(
        PresentationRequest request,
        PresentationSource source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(source.Text))
            throw new AnalysisProviderException("The presentation source contains no usable content.");

        var configuredProvider = configuration["Analysis:Provider"]?.Trim();
        var providerId = string.IsNullOrWhiteSpace(configuredProvider) ||
                         configuredProvider.Equals("Configured", StringComparison.OrdinalIgnoreCase)
            ? providerConfiguration.ProviderOrder[0]
            : configuredProvider;

        var slideCount = request.SlideCount;
        if (slideCount is < 5 or > 30)
            throw new AnalysisProviderException("Presentation slide count must be between 5 and 30.");

        var system = """
            You are an enterprise presentation architect. Use only facts grounded in the supplied source.
            Do not invent features, metrics, architecture, screenshots or organization facts. Create concise,
            visual, executive-quality slide content and speaker notes. Return valid JSON only.
            """;
        var prompt = $$"""
            Create a {{slideCount}}-slide professional presentation plan.

            Title: {{request.Title}}
            Audience: {{request.TargetAudience}}
            Purpose: {{request.Purpose}}
            Language: {{request.Language}}
            Aspect ratio: {{request.AspectRatio}}
            Include speaker notes: {{request.IncludeSpeakerNotes}}
            Include architecture diagrams: {{request.IncludeArchitectureDiagrams}}
            Include application screenshots: {{request.IncludeScreenshots}}
            Source type: {{source.SourceType}}
            Source reference: {{source.SourceReference}}

            Select relevant layouts dynamically: Title, ExecutiveSummary, BusinessProblem, ProposedSolution,
            ProjectOverview, Features, ArchitectureDiagram, TechnologyStack, Workflow, AIArchitecture,
            DataFlow, DatabaseArchitecture, Screenshot, Analytics, RiskAssessment, Benefits, Roadmap,
            FutureEnhancements, Conclusion, QA.

            Create this JSON object:
            {
              "description":"one sentence",
              "slides":[{
                "slideNumber":1,
                "title":"short title",
                "subtitle":"optional subtitle",
                "layoutType":"Title",
                "content":["2 to 5 concise bullet points"],
                "visualElements":[{"type":"Card","label":"Label","value":"Value"}],
                "diagram":{"type":"Workflow","mermaid":"flowchart TD\n A[Source] --> B[Result]"},
                "chart":{"type":"Bar","labels":["A","B"],"values":[1,2]},
                "speakerNotes":"presentation narration grounded in the slide",
                "sourceReferences":["source file or section name"]
              }]
            }

            Return exactly {{slideCount}} slides. Keep slide content concise: at most five bullets and no paragraph
            longer than 220 characters. Only provide chart data or visual metrics that appear in the source.
            Do not fabricate analytics. Architecture diagrams must reflect source facts. Include a title slide,
            relevant middle slides, and a conclusion or Q&A. Never claim inaccessible data was observed.

            SOURCE CONTENT:
            {{source.Text[..Math.Min(source.Text.Length, 60000)]}}
            """;

        var inference = await inferenceRouter.GenerateJsonAsync<PresentationPlan>(
            providerId,
            system,
            prompt,
            request.AllowCloudProcessing,
            cancellationToken,
            plan =>
            {
                if (plan.Slides is null || plan.Slides.Count != slideCount)
                    throw new AnalysisProviderException(
                        $"The model returned {plan.Slides?.Count ?? 0} slides; {slideCount} were requested.");
                return plan;
            });
        var plan = inference.Value;

        var slides = plan.Slides
            .Select((slide, index) => new PresentationSlide
            {
                SlideNumber = index + 1,
                Title = slide.Title,
                Subtitle = slide.Subtitle,
                LayoutType = slide.LayoutType,
                Content = slide.Content.Take(5).Select(item => item.Trim()).Where(item => item.Length > 0).ToList(),
                VisualElements = slide.VisualElements.Take(6).ToList(),
                Diagram = request.IncludeArchitectureDiagrams ? slide.Diagram : null,
                Chart = slide.Chart,
                ScreenshotReferences = request.IncludeScreenshots ? slide.ScreenshotReferences : [],
                SpeakerNotes = request.IncludeSpeakerNotes ? slide.SpeakerNotes : "",
                SourceReferences = slide.SourceReferences.Take(10).ToList()
            })
            .ToList();

        return new PresentationDocument
        {
            Title = request.Title,
            Description = plan.Description,
            SourceType = source.SourceType,
            SourceReference = source.SourceReference,
            TargetAudience = request.TargetAudience,
            Theme = "AI_Digest Navy Blue",
            AspectRatio = request.AspectRatio,
            TotalSlides = slides.Count,
            AIProvider = inference.ProviderName,
            AIModel = inference.ModelName,
            Slides = slides
        };
    }

    private sealed class PresentationPlan
    {
        public string Description { get; init; } = "";
        public List<PresentationSlidePlan> Slides { get; init; } = [];
    }

    private sealed class PresentationSlidePlan
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
}
