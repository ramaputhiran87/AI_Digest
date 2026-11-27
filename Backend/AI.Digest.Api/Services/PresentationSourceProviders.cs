using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed class PresentationSourceProviders(HttpClient httpClient, IConfiguration configuration)
{
    public async Task<PresentationSource> GetSourceAsync(
        PresentationRequest request,
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        var type = request.SourceType.Trim();
        return type switch
        {
            "Markdown" or "Text" => await MarkdownAsync(file, cancellationToken),
            "GitHub" => await GitHubAsync(request.SourceReference, cancellationToken),
            "ApplicationUrl" => await ApplicationUrlAsync(request.SourceReference, cancellationToken),
            "AnalysisResult" => AnalysisResult(request),
            "UseCase" => new PresentationSource
            {
                SourceType = "UseCase",
                SourceReference = "Use-case description",
                Text = request.UseCase ?? "",
                SourceFiles = ["Use-case description"]
            },
            _ => throw new AnalysisProviderException("Unsupported presentation source type.")
        };
    }

    private static PresentationSource AnalysisResult(PresentationRequest request)
    {
        var context = request.AnalysisContext?.Trim();
        if (string.IsNullOrWhiteSpace(context))
            throw new AnalysisProviderException(
                "A completed analysis result is required for this presentation source.",
                failureCode: AnalysisFailureCode.InvalidInput);
        if (context.Length > 60_000)
            throw new AnalysisProviderException(
                "The structured analysis result exceeds the presentation context limit.",
                failureCode: AnalysisFailureCode.InvalidInput);

        return new PresentationSource
        {
            SourceType = "AnalysisResult",
            SourceReference = string.IsNullOrWhiteSpace(request.SourceReference)
                ? "Previously completed analysis"
                : request.SourceReference,
            Text = context,
            SourceFiles = [request.SourceReference]
        };
    }

    private static async Task<PresentationSource> MarkdownAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null) throw new AnalysisProviderException("A Markdown or text file is required.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".md" or ".markdown" or ".txt"))
            throw new AnalysisProviderException("Supported Markdown files are MD, MARKDOWN, and TXT.");
        if (file.Length > 5 * 1024 * 1024)
            throw new AnalysisProviderException("Presentation source files are limited to 5 MB.");
        await using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) throw new AnalysisProviderException("The source file is empty.");
        return new PresentationSource
        {
            SourceType = "Markdown",
            SourceReference = file.FileName,
            Text = text,
            SourceFiles = [file.FileName]
        };
    }

    private async Task<PresentationSource> GitHubAsync(string repositoryUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var sourceUri) ||
            !sourceUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new AnalysisProviderException("Only github.com repository URLs are supported.");

        var segments = sourceUri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2) throw new AnalysisProviderException("The GitHub repository URL is incomplete.");
        var owner = segments[0];
        var repo = segments[1].TrimEnd('.');

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(repo)}/contents");
        request.Headers.UserAgent.ParseAdd("AI-Digest-Presentation-Studio");
        var token = configuration["GitHub:Token"];
        if (!string.IsNullOrWhiteSpace(token)) request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AnalysisProviderException($"GitHub repository could not be retrieved (HTTP {(int)response.StatusCode}).");

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var selected = new List<(string Name, string DownloadUrl)>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.GetProperty("type").GetString() != "file") continue;
            var name = item.GetProperty("name").GetString() ?? "";
            if (Regex.IsMatch(name, @"^(README|ARCHITECTURE|package\.json|.*\.csproj|.*\.sln|.*\.ts|.*\.scss|.*\.html)$", RegexOptions.IgnoreCase))
            {
                if (item.TryGetProperty("download_url", out var downloadUrl) && downloadUrl.ValueKind == JsonValueKind.String)
                    selected.Add((name, downloadUrl.GetString() ?? ""));
            }
            if (selected.Count >= 12) break;
        }

        var content = new System.Text.StringBuilder();
        content.AppendLine($"Repository: https://github.com/{owner}/{repo}");
        foreach (var (name, downloadUrl) in selected.Where(item => !string.IsNullOrWhiteSpace(item.DownloadUrl)).Take(12))
        {
            try
            {
                var text = await httpClient.GetStringAsync(downloadUrl, cancellationToken);
                content.AppendLine($"\n## Repository file: {name}\n{text[..Math.Min(text.Length, 18000)]}");
            }
            catch (HttpRequestException) { }
        }

        if (content.Length < 100)
            throw new AnalysisProviderException("The repository had no accessible README or supported source files.");
        return new PresentationSource
        {
            SourceType = "GitHub",
            SourceReference = repositoryUrl,
            Text = content.ToString(),
            SourceFiles = selected.Select(item => item.Name).Distinct().Take(12).ToList()
        };
    }

    private async Task<PresentationSource> ApplicationUrlAsync(string applicationUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(applicationUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new AnalysisProviderException("Application URLs must use HTTP or HTTPS.");
        if (await IsPrivateOrBlockedUriAsync(uri, cancellationToken))
            throw new AnalysisProviderException("Private, loopback and infrastructure network targets require explicit authorized local inspection and are blocked by default.");

        using var response = await httpClient.GetAsync(uri, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new AnalysisProviderException($"Application URL could not be retrieved (HTTP {(int)response.StatusCode}).");
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var text = Regex.Replace(html, @"<script[^>]*>.*?</script>|<style[^>]*>.*?</style>|<[^>]+>|&nbsp;", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > 50000) text = text[..50000];
        if (string.IsNullOrWhiteSpace(text)) throw new AnalysisProviderException("The application page returned no accessible text.");
        return new PresentationSource
        {
            SourceType = "ApplicationUrl",
            SourceReference = applicationUrl,
            Text = text,
            SourceFiles = [applicationUrl]
        };
    }

    private static async Task<bool> IsPrivateOrBlockedUriAsync(Uri uri, CancellationToken cancellationToken)
    {
        if (uri.HostNameType != UriHostNameType.Dns) return true;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);
            foreach (var address in addresses)
            {
                if (IPAddress.IsLoopback(address)) return true;
                var bytes = address.GetAddressBytes();
                if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    if (bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254) ||
                        (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                        (bytes[0] == 192 && bytes[1] == 168)) return true;
                }
            }
        }
        catch (System.Net.Sockets.SocketException) { return true; }
        return false;
    }
}
