using AI.Digest.Api.Data;
using AI.Digest.Api.Models;
using AI.Digest.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMicrosoftIdentityWebApiAuthentication(
    builder.Configuration.GetSection("AzureAd"));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(builder.Configuration["Cors:Origins"]?.Split(',') ?? ["http://localhost:4200"])
          .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddDbContext<DigestDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DigestDb")));
builder.Services.AddSingleton<AgentServices>();
builder.Services.AddScoped<DocumentAnalysisService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapOpenApi();

app.MapPost("/api/documents/analyse", async (HttpRequest request,
    DocumentAnalysisService analysis) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest(new { error = "Multipart document upload required." });

    var form = await request.ReadFormAsync();
    var file = form.Files["file"];
    var useCase = form["useCase"].ToString();

    if (file is null)
        return Results.BadRequest(new { error = "A document or use-case text is required." });

    await using var stream = file.OpenReadStream();
    using var reader = new StreamReader(stream);
    var content = await reader.ReadToEndAsync();
    var result = await analysis.AnalyseAsync(file.FileName, content, useCase);
    return Results.Ok(result);
}).AllowAnonymous();

app.MapPost("/api/documents/analyse-text", async (AnalyseTextRequest request,
    DocumentAnalysisService analysis) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest(new { error = "Use-case text is required." });
    var result = await analysis.AnalyseAsync(
        string.IsNullOrWhiteSpace(request.Title) ? "Use-case.txt" : request.Title,
        request.Text,
        request.UseCase);
    return Results.Ok(result);
}).AllowAnonymous();

app.MapGet("/api/documents/{id}/source/{line}", (int id, int line) =>
    Results.Ok(new { documentId = id, line, sourceAvailable = true }));

app.MapPost("/api/documents/export/json", (DigestResult result) =>
    Results.Json(result));

app.Run();
