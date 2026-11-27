using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed class PresentationStudioService(
    PresentationSourceProviders sources,
    PresentationPlanningService planner,
    PresentationGenerationService generator,
    PresentationHistoryStore historyStore)
{
    public IReadOnlyCollection<PresentationDocument> History => historyStore.History;

    public async Task<PresentationDocument> PreviewAsync(
        PresentationRequest request,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        request = ValidateRequest(request);
        var source = await sources.GetSourceAsync(request, file, cancellationToken);
        var presentation = await planner.PlanAsync(request, source, cancellationToken);
        presentation = generator.Validate(presentation);
        historyStore.Save(presentation);
        return presentation;
    }

    public PresentationExportResult Export(PresentationDocument presentation)
    {
        presentation = generator.Validate(presentation);
        historyStore.Save(presentation);
        return new PresentationExportResult
        {
            PresentationId = presentation.Id,
            PptxFileName = SafeName(presentation.Title) + ".pptx",
            PdfFileName = SafeName(presentation.Title) + ".pdf",
            Presentation = presentation
        };
    }

    public PresentationDocument Get(Guid id) => historyStore.Get(id);

    public bool Delete(Guid id) => historyStore.Delete(id);

    public void Update(PresentationDocument presentation)
    {
        presentation = generator.Validate(presentation);
        historyStore.Save(presentation);
    }

    public byte[] Pptx(PresentationDocument presentation) => generator.GeneratePptx(presentation);
    public byte[] Pdf(PresentationDocument presentation) => generator.GeneratePdf(presentation);

    public static PresentationRequest ValidateRequest(PresentationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new AnalysisProviderException(
                "Presentation title is required.",
                failureCode: AnalysisFailureCode.InvalidInput);
        if (request.SourceType == "UseCase" && string.IsNullOrWhiteSpace(request.UseCase))
            throw new AnalysisProviderException(
                "Use-case description is required.",
                failureCode: AnalysisFailureCode.InvalidInput);
        if (request.SourceType == "GitHub" && string.IsNullOrWhiteSpace(request.SourceReference))
            throw new AnalysisProviderException(
                "GitHub repository URL is required.",
                failureCode: AnalysisFailureCode.InvalidInput);
        if (request.SourceType == "ApplicationUrl" && string.IsNullOrWhiteSpace(request.SourceReference))
            throw new AnalysisProviderException(
                "Application URL is required.",
                failureCode: AnalysisFailureCode.InvalidInput);
        var supportedThemes = new[]
        {
            "Auto", "Auto (AI recommendation)", "Neural Intelligence",
            "Cinematic Product Launch", "Executive Gold", "Digital Blueprint",
            "Immersive Storytelling", "Knowledge Universe", "AI_Digest Navy Blue",
            "Enterprise Light", "Executive Dark", "Technology Blue", "Modern Minimal"
        };
        if (!string.IsNullOrWhiteSpace(request.Theme) &&
            !supportedThemes.Contains(request.Theme.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new AnalysisProviderException(
                "The selected presentation theme is not supported.",
                failureCode: AnalysisFailureCode.InvalidInput);
        }
        return request;
    }

    private static string SafeName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Where(character => !invalid.Contains(character) && character != ':').ToArray());
        name = string.Join(" ", name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(name) ? "AI Presentation" : name[..Math.Min(name.Length, 80)];
    }
}

public sealed class PresentationHistoryStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, PresentationDocument> history = new();

    public IReadOnlyCollection<PresentationDocument> History => history.Values
        .OrderByDescending(presentation => presentation.CreatedAt)
        .ToList();

    public void Save(PresentationDocument presentation) =>
        history[presentation.Id] = presentation;

    public PresentationDocument Get(Guid id) =>
        history.TryGetValue(id, out var presentation)
            ? presentation
            : throw new KeyNotFoundException("Presentation was not found.");

    public bool Delete(Guid id) => history.TryRemove(id, out _);
}
