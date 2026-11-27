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

    public IReadOnlyCollection<PresentationVersionSummary> Versions(Guid id) => historyStore.Versions(id);

    public PresentationDocument GetVersion(Guid id, Guid versionId) => historyStore.GetVersion(id, versionId);
    public PresentationDocument RestoreVersion(Guid id, Guid versionId) =>
        historyStore.RestoreVersion(id, versionId);

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
            "Enterprise Light", "Executive Dark", "Technology Blue", "Modern Minimal", "Prism Blue"
        };
        if (!string.IsNullOrWhiteSpace(request.Theme) &&
            !supportedThemes.Contains(request.Theme.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new AnalysisProviderException(
                "The selected presentation theme is not supported.",
                failureCode: AnalysisFailureCode.InvalidInput);
        }
        if (request.UserInstructions is { Length: > 5000 } ||
            request.VisualIntensity is not ("Minimal" or "Balanced" or "Expressive"))
            throw new AnalysisProviderException(
                "Presentation instructions or visual intensity are invalid.",
                failureCode: AnalysisFailureCode.InvalidInput);
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
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, List<PresentationVersion>> versions = new();

    public IReadOnlyCollection<PresentationDocument> History => history.Values
        .OrderByDescending(presentation => presentation.CreatedAt)
        .ToList();

    public void Save(PresentationDocument presentation)
    {
        if (history.TryGetValue(presentation.Id, out var previous) &&
            !ReferenceEquals(previous, presentation))
        {
            var snapshots = versions.GetOrAdd(presentation.Id, _ => []);
            lock (snapshots)
            {
                snapshots.Add(new PresentationVersion
                {
                    Label = snapshots.Count == 0 ? "Original AI draft" : "Saved revision",
                    Presentation = previous
                });
                if (snapshots.Count > 30) snapshots.RemoveAt(0);
            }
        }
        history[presentation.Id] = presentation;
    }

    public IReadOnlyCollection<PresentationVersionSummary> Versions(Guid id)
    {
        if (!history.ContainsKey(id)) throw new KeyNotFoundException("Presentation was not found.");
        var snapshots = versions.GetOrAdd(id, _ => []);
        lock (snapshots) return snapshots
            .Select(snapshot => new PresentationVersionSummary
            {
                Id = snapshot.Id,
                CreatedAt = snapshot.CreatedAt,
                Label = snapshot.Label
            })
            .ToList();
    }

    public PresentationDocument GetVersion(Guid id, Guid versionId)
    {
        if (!history.ContainsKey(id)) throw new KeyNotFoundException("Presentation was not found.");
        var snapshots = versions.GetOrAdd(id, _ => []);
        lock (snapshots)
            return snapshots.FirstOrDefault(snapshot => snapshot.Id == versionId)?.Presentation ??
                throw new KeyNotFoundException("Presentation version was not found.");
    }

    public PresentationDocument RestoreVersion(Guid id, Guid versionId)
    {
        var snapshots = versions.GetOrAdd(id, _ => []);
        lock (snapshots)
        {
            var version = snapshots.FirstOrDefault(snapshot => snapshot.Id == versionId) ??
                throw new KeyNotFoundException("Presentation version was not found.");
            Save(version.Presentation);
            return version.Presentation;
        }
    }

    public PresentationDocument Get(Guid id) =>
        history.TryGetValue(id, out var presentation)
            ? presentation
            : throw new KeyNotFoundException("Presentation was not found.");

    public bool Delete(Guid id)
    {
        versions.TryRemove(id, out _);
        return history.TryRemove(id, out _);
    }
}
