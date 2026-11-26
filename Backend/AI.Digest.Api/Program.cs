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

app.Run();

public partial class Program { }
