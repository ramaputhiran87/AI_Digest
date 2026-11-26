using AI.Digest.Api.Data;
using AI.Digest.Api.Models;
using AI.Digest.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

var entraConfigured =
    !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:TenantId"]) &&
    !string.IsNullOrWhiteSpace(builder.Configuration["AzureAd:ClientId"]);
if (entraConfigured)
{
    builder.Services.AddMicrosoftIdentityWebApiAuthentication(
        builder.Configuration.GetSection("AzureAd"));
}
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Cors:Origins"]?.Split(',') ?? ["http://localhost:4200"])
          .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddDbContext<DigestDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DigestDb")));
builder.Services.AddSingleton<AgentServices>();
builder.Services.AddScoped<DeterministicAnalysisProvider>();
builder.Services.AddHttpClient<GeminiAnalysisProvider>(client =>
    client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<OllamaAnalysisProvider>(client =>
    client.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddScoped<IAiModelClient>(services =>
    services.GetRequiredService<GeminiAnalysisProvider>());
builder.Services.AddScoped<IAiModelClient>(services =>
    services.GetRequiredService<OllamaAnalysisProvider>());
builder.Services.AddScoped<LlmAnalysisProvider>();
builder.Services.AddScoped<IAnalysisProvider, AnalysisProviderRouter>();
builder.Services.AddScoped<DocumentAnalysisService>();
builder.Services.AddHttpClient<PresentationSourceProviders>(client =>
    client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddScoped<PresentationPlanningService>();
builder.Services.AddScoped<PresentationGenerationService>();
builder.Services.AddSingleton<PresentationStudioService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseCors();
if (entraConfigured)
{
    app.UseAuthentication();
}
app.UseAuthorization();
app.MapOpenApi();

app.MapPost("/api/documents/analyse", async (HttpRequest request,
    DocumentAnalysisService analysis,
    CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Multipart document upload required." });

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["file"];
    var useCase = form["useCase"].ToString();
    var analysisMode = form["analysisMode"].ToString();

    if (file is null)
        return Results.BadRequest(new { error = "A document or use-case text is required." });
    if (file.Length > 20 * 1024 * 1024)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    try
    {
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var result = await analysis.AnalyseAsync(
            file.FileName, content, useCase, cancellationToken, analysisMode);
        return Results.Ok(result);
    }
    catch (AnalysisProviderException exception)
    {
        return Results.Json(new
        {
            error = "AI analysis unavailable – provider error.",
            detail = exception.Message
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapPost("/api/documents/analyse-text", async (AnalyseTextRequest request,
    DocumentAnalysisService analysis,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest(new { error = "Use-case text is required." });
    try
    {
        var result = await analysis.AnalyseAsync(
            string.IsNullOrWhiteSpace(request.Title) ? "Use-case.txt" : request.Title,
            request.Text,
            request.UseCase,
            cancellationToken,
            request.AnalysisMode);
        return Results.Ok(result);
    }
    catch (AnalysisProviderException exception)
    {
        return Results.Json(new
        {
            error = "AI analysis unavailable – provider error.",
            detail = exception.Message
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();

app.MapGet("/api/documents/{id}/source/{line}", (int id, int line) =>
    Results.Ok(new { documentId = id, line, sourceAvailable = true }));

app.MapPost("/api/documents/export/json", (DigestResult result) =>
    Results.Json(result));

app.MapPost("/api/presentations/preview", async (HttpRequest request,
    PresentationStudioService studio,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var presentationRequest = new PresentationRequest
            {
                SourceType = form["sourceType"].ToString(),
                SourceReference = form["sourceReference"].ToString(),
                UseCase = form["useCase"].ToString(),
                Title = form["title"].ToString(),
                TargetAudience = form["targetAudience"].ToString(),
                Purpose = form["purpose"].ToString(),
                SlideCount = int.TryParse(form["slideCount"], out var count) ? count : 10,
                Language = form["language"].ToString(),
                AspectRatio = form["aspectRatio"].ToString(),
                IncludeSpeakerNotes = bool.TryParse(form["includeSpeakerNotes"], out var notes) && notes,
                IncludeArchitectureDiagrams = bool.TryParse(form["includeArchitectureDiagrams"], out var diagrams) && diagrams,
                IncludeScreenshots = bool.TryParse(form["includeScreenshots"], out var screenshots) && screenshots
            };
            var result = await studio.PreviewAsync(presentationRequest, form.Files["file"], cancellationToken);
            return Results.Ok(result);
        }

        var jsonRequest = await request.ReadFromJsonAsync<PresentationRequest>(cancellationToken);
        if (jsonRequest is null)
            return Results.BadRequest(new { error = "A presentation request is required." });
        var jsonResult = await studio.PreviewAsync(jsonRequest, null, cancellationToken);
        return Results.Ok(jsonResult);
    }
    catch (AnalysisProviderException exception)
    {
        return Results.Json(new
        {
            error = "AI presentation unavailable – provider error.",
            detail = exception.Message
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous().DisableAntiforgery();

app.MapPut("/api/presentations/{id:guid}", (Guid id,
    PresentationDocument presentation,
    PresentationStudioService studio) =>
{
    try
    {
        if (presentation.Id != id)
            return Results.BadRequest(new { error = "Presentation ID mismatch." });
        presentation = new PresentationDocument
        {
            Id = presentation.Id,
            Title = presentation.Title,
            Description = presentation.Description,
            SourceType = presentation.SourceType,
            SourceReference = presentation.SourceReference,
            TargetAudience = presentation.TargetAudience,
            Theme = presentation.Theme,
            AspectRatio = presentation.AspectRatio,
            TotalSlides = presentation.Slides.Count,
            CreatedAt = presentation.CreatedAt,
            AIProvider = presentation.AIProvider,
            AIModel = presentation.AIModel,
            Status = presentation.Status,
            Slides = presentation.Slides
        };
        studio.Update(presentation);
        return Results.Ok(presentation);
    }
    catch (AnalysisProviderException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).AllowAnonymous();

app.MapGet("/api/presentations", (PresentationStudioService studio) =>
    Results.Ok(studio.History)).AllowAnonymous();

app.MapGet("/api/presentations/{id:guid}", (Guid id, PresentationStudioService studio) =>
{
    try { return Results.Ok(studio.Get(id)); }
    catch (KeyNotFoundException) { return Results.NotFound(); }
}).AllowAnonymous();

app.MapGet("/api/presentations/{id:guid}/pptx", (Guid id,
    PresentationStudioService studio) =>
{
    try
    {
        var presentation = studio.Get(id);
        var bytes = studio.Pptx(presentation);
        var name = presentation.Title.Replace(" ", "-").Replace("/", "-") + ".pptx";
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.presentationml.presentation", name);
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (AnalysisProviderException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).AllowAnonymous();

app.MapGet("/api/presentations/{id:guid}/pdf", (Guid id,
    PresentationStudioService studio) =>
{
    try
    {
        var presentation = studio.Get(id);
        var bytes = studio.Pdf(presentation);
        var name = presentation.Title.Replace(" ", "-").Replace("/", "-") + ".pdf";
        return Results.File(bytes, "application/pdf", name);
    }
    catch (KeyNotFoundException) { return Results.NotFound(); }
    catch (AnalysisProviderException exception)
    {
        return Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status400BadRequest);
    }
}).AllowAnonymous();

app.MapDelete("/api/presentations/{id:guid}", (Guid id,
    PresentationStudioService studio) =>
    studio.Delete(id) ? Results.NoContent() : Results.NotFound()).AllowAnonymous();

app.Run();

public partial class Program { }
