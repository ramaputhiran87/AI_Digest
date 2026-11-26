using System.Collections.Concurrent;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed class PresentationStudioService(
    PresentationSourceProviders sources,
    PresentationPlanningService planner,
    PresentationGenerationService generator)
{
    private readonly ConcurrentDictionary<Guid, PresentationDocument> history = new();

    public IReadOnlyCollection<PresentationDocument> History => history.Values
        .OrderByDescending(presentation => presentation.CreatedAt)
        .ToList();

    public async Task<PresentationDocument> PreviewAsync(
        PresentationRequest request,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        request = ValidateRequest(request);
        var source = await sources.GetSourceAsync(request, file, cancellationToken);
        var presentation = await planner.PlanAsync(request, source, cancellationToken);
        presentation = generator.Validate(presentation);
        history[presentation.Id] = presentation;
        return presentation;
    }

    public PresentationExportResult Export(PresentationDocument presentation)
    {
        presentation = generator.Validate(presentation);
        history[presentation.Id] = presentation;
        return new PresentationExportResult
        {
            PresentationId = presentation.Id,
            PptxFileName = SafeName(presentation.Title) + ".pptx",
            PdfFileName = SafeName(presentation.Title) + ".pdf",
            Presentation = presentation
        };
    }

    public PresentationDocument Get(Guid id) =>
        history.TryGetValue(id, out var presentation)
            ? presentation
            : throw new KeyNotFoundException("Presentation was not found.");

    public bool Delete(Guid id) => history.TryRemove(id, out _);

    public void Update(PresentationDocument presentation)
    {
        presentation = generator.Validate(presentation);
        history[presentation.Id] = presentation;
    }

    public byte[] Pptx(PresentationDocument presentation) => generator.GeneratePptx(presentation);
    public byte[] Pdf(PresentationDocument presentation) => generator.GeneratePdf(presentation);

    private static PresentationRequest ValidateRequest(PresentationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new AnalysisProviderException("Presentation title is required.");
        if (request.SourceType == "UseCase" && string.IsNullOrWhiteSpace(request.UseCase))
            throw new AnalysisProviderException("Use-case description is required.");
        if (request.SourceType == "GitHub" && string.IsNullOrWhiteSpace(request.SourceReference))
            throw new AnalysisProviderException("GitHub repository URL is required.");
        if (request.SourceType == "ApplicationUrl" && string.IsNullOrWhiteSpace(request.SourceReference))
            throw new AnalysisProviderException("Application URL is required.");
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
