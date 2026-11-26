using System.ComponentModel;
using System.Diagnostics;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public sealed class CodexCliAnalysisProvider(IConfiguration configuration) : IAiModelClient
{
    public string ProviderId => "Codex";
    public string ProviderName => "OpenAI Codex (ChatGPT sign-in)";
    public string ModelName =>
        configuration["AI:Providers:Codex:Model"] ??
        configuration["Codex:Model"] ??
        "gpt-6.1-sol";
    public bool IsConfigured
    {
        get
        {
            var executable = configuration["Codex:Command"] ?? "codex";
            if (Path.IsPathRooted(executable))
                return File.Exists(executable);

            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            return path.Split(Path.PathSeparator)
                .Any(directory => File.Exists(Path.Combine(directory, executable + ".exe")));
        }
    }

    public async Task<ModelInferenceResult> GenerateJsonAsync(
        string systemInstruction,
        string prompt,
        CancellationToken cancellationToken)
    {
        var executable = configuration["Codex:Command"] ?? "codex";
        var timeoutMinutes = Math.Clamp(configuration.GetValue("Codex:TimeoutMinutes", 5), 1, 30);
        var outputPath = Path.Combine(Path.GetTempPath(), $"ai-digest-codex-{Guid.NewGuid():N}.json");
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetTempPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment.Remove("OPENAI_API_KEY");
        startInfo.Environment.Remove("CODEX_API_KEY");
        startInfo.Environment.Remove("OPENAI_BASE_URL");
        using var process = new Process
        {
            StartInfo = startInfo
        };
        process.StartInfo.ArgumentList.Add("exec");
        process.StartInfo.ArgumentList.Add("--model");
        process.StartInfo.ArgumentList.Add(ModelName);
        process.StartInfo.ArgumentList.Add("--json");
        process.StartInfo.ArgumentList.Add("--sandbox");
        process.StartInfo.ArgumentList.Add("read-only");
        process.StartInfo.ArgumentList.Add("--ask-for-approval");
        process.StartInfo.ArgumentList.Add("never");
        process.StartInfo.ArgumentList.Add("--ephemeral");
        process.StartInfo.ArgumentList.Add("--ignore-user-config");
        process.StartInfo.ArgumentList.Add("--skip-git-repo-check");
        process.StartInfo.ArgumentList.Add("--output-last-message");
        process.StartInfo.ArgumentList.Add(outputPath);
        process.StartInfo.ArgumentList.Add("-");

        try
        {
            try
            {
                if (!process.Start())
                {
                    throw new AnalysisProviderException("Codex CLI could not be started.");
                }
            }
            catch (Win32Exception exception)
            {
                throw new AnalysisProviderException(
                    "Codex CLI is not installed or is not on the API server's PATH. Install Codex CLI and run `codex login`.",
                    exception);
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));
            var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.StandardInput.WriteAsync(
                    $"{systemInstruction}\n\n{prompt}".AsMemory(), timeout.Token);
            }
            catch (IOException exception)
            {
                throw new AnalysisProviderException(
                    "Codex CLI closed its input before accepting the analysis request.",
                    exception,
                    providerId: ProviderId);
            }
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(standardOutput, standardError);

            if (process.ExitCode != 0)
            {
                throw new AnalysisProviderException(
                    $"Codex CLI exited with code {process.ExitCode}. Check `codex login status`, model availability, and your ChatGPT plan.");
            }

            if (!File.Exists(outputPath))
            {
                throw new AnalysisProviderException("Codex CLI completed without producing a final analysis.");
            }

            var output = (await File.ReadAllTextAsync(outputPath, cancellationToken)).Trim();
            output = RemoveJsonCodeFence(output);
            if (string.IsNullOrWhiteSpace(output))
            {
                throw new AnalysisProviderException("Codex CLI returned an empty analysis.");
            }

            return new ModelInferenceResult(output, ModelName, null, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw new AnalysisProviderException($"Codex CLI analysis exceeded its {timeoutMinutes}-minute timeout.");
        }
        finally
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
    }

    private static string RemoveJsonCodeFence(string output)
    {
        if (!output.StartsWith("```", StringComparison.Ordinal) ||
            !output.EndsWith("```", StringComparison.Ordinal))
        {
            return output;
        }

        var firstNewLine = output.IndexOf('\n');
        return firstNewLine < 0
            ? output
            : output[(firstNewLine + 1)..^3].Trim();
    }
}
