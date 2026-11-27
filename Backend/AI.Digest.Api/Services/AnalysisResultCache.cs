using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AI.Digest.Api.Models;
using Microsoft.Extensions.Caching.Memory;

namespace AI.Digest.Api.Services;

public sealed class AnalysisResultCache(
    IMemoryCache cache,
    IConfiguration configuration,
    ILogger<AnalysisResultCache>? logger = null)
{
    private readonly ConcurrentDictionary<string, SharedOperation> inFlight =
        new(StringComparer.Ordinal);

    public Task<DigestResult> GetOrCreateAsync(
        string userScope,
        string title,
        string content,
        string? useCase,
        string? analysisMode,
        bool allowCloudProcessing,
        Func<Task<DigestResult>> create,
        string? analysisDepth = null) =>
        GetOrCreateAsync(
            userScope,
            title,
            content,
            useCase,
            analysisMode,
            allowCloudProcessing,
            (_, _) => create(),
            analysisDepth,
            CancellationToken.None,
            null);

    public Task<DigestResult> GetOrCreateAsync(
        string userScope,
        string title,
        string content,
        string? useCase,
        string? analysisMode,
        bool allowCloudProcessing,
        Func<CancellationToken, Task<DigestResult>> create,
        string? analysisDepth,
        CancellationToken cancellationToken) =>
        GetOrCreateAsync(
            userScope,
            title,
            content,
            useCase,
            analysisMode,
            allowCloudProcessing,
            (sharedCancellationToken, _) => create(sharedCancellationToken),
            analysisDepth,
            cancellationToken,
            null);

    public async Task<DigestResult> GetOrCreateAsync(
        string userScope,
        string title,
        string content,
        string? useCase,
        string? analysisMode,
        bool allowCloudProcessing,
        Func<CancellationToken, IProgress<AnalysisProgressEvent>?, Task<DigestResult>> create,
        string? analysisDepth,
        CancellationToken cancellationToken,
        IProgress<AnalysisProgressEvent>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = BuildKey(
            userScope, title, content, useCase, analysisMode, allowCloudProcessing, analysisDepth);
        if (cache.TryGetValue(key, out DigestResult? cached) && cached is not null)
        {
            logger?.LogInformation(
                "Analysis result cache hit. AnalysisDepth={AnalysisDepth}.",
                analysisDepth ?? "Quick");
            return cached;
        }

        var isNewOperation = false;
        if (!inFlight.TryGetValue(key, out var operation))
        {
            var candidate = new SharedOperation();
            operation = inFlight.GetOrAdd(key, candidate);
            isNewOperation = ReferenceEquals(operation, candidate);
            if (!isNewOperation)
                candidate.DisposeUnstarted();
        }
        logger?.LogInformation(
            "Analysis result cache {CacheStatus}. AnalysisDepth={AnalysisDepth}.",
            isNewOperation ? "miss" : "in-flight deduplication",
            analysisDepth ?? "Quick");
        Action<AnalysisProgressEvent>? reportProgress =
            progress is null ? null : progress.Report;
        var task = operation.JoinOrStart(reportProgress, async (sharedCancellationToken, sharedProgress) =>
        {
            try
            {
                var result = await create(sharedCancellationToken, sharedProgress);
                var expirationMinutes = Math.Clamp(
                    configuration.GetValue("AI:CacheDurationMinutes", 10), 0, 60);
                if (expirationMinutes > 0)
                {
                    cache.Set(
                        key,
                        result,
                        new MemoryCacheEntryOptions()
                            .SetAbsoluteExpiration(TimeSpan.FromMinutes(expirationMinutes))
                            .SetSize(1));
                }

                return result;
            }
            finally
            {
                ((ICollection<KeyValuePair<string, SharedOperation>>)inFlight)
                    .Remove(new KeyValuePair<string, SharedOperation>(key, operation));
            }
        });
        try
        {
            return await task.WaitAsync(cancellationToken);
        }
        finally
        {
            operation.Release(cancellationToken.IsCancellationRequested, reportProgress);
        }
    }

    private sealed class SharedOperation
    {
        private readonly object gate = new();
        private readonly CancellationTokenSource cancellation = new();
        private readonly List<Action<AnalysisProgressEvent>> progressListeners = [];
        private readonly List<AnalysisProgressEvent> progressHistory = [];
        private Task<DigestResult>? task;
        private int waiters;

        public Task<DigestResult> JoinOrStart(
            Action<AnalysisProgressEvent>? progress,
            Func<CancellationToken, IProgress<AnalysisProgressEvent>?, Task<DigestResult>> create)
        {
            lock (gate)
            {
                waiters++;
                if (progress is not null)
                {
                    progressListeners.Add(progress);
                    foreach (var item in progressHistory)
                        progress(item);
                }
                if (task is null)
                {
                    var reporter = new InlineProgress<AnalysisProgressEvent>(Publish);
                    task = Task.Run(
                        () => create(cancellation.Token, reporter),
                        CancellationToken.None);
                    _ = task.ContinueWith(
                        _ => cancellation.Dispose(),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                return task;
            }
        }

        public void Publish(AnalysisProgressEvent progress)
        {
            Action<AnalysisProgressEvent>[] listeners;
            lock (gate)
            {
                progressHistory.Add(progress);
                if (progressHistory.Count > 256)
                    progressHistory.RemoveRange(0, progressHistory.Count - 256);
                listeners = progressListeners.ToArray();
            }

            foreach (var listener in listeners)
                listener(progress);
        }

        public void Release(
            bool callerCancelled,
            Action<AnalysisProgressEvent>? progress)
        {
            lock (gate)
            {
                if (progress is not null)
                    progressListeners.Remove(progress);
                waiters--;
                if (callerCancelled && waiters == 0 && task is { IsCompleted: false })
                    cancellation.Cancel();
            }
        }

        public void DisposeUnstarted() => cancellation.Dispose();
    }

    private string BuildKey(
        string userScope,
        string title,
        string content,
        string? useCase,
        string? analysisMode,
        bool allowCloudProcessing,
        string? analysisDepth)
    {
        var input = string.Join(
            '\0',
            userScope,
            title,
            content,
            useCase ?? "",
            analysisMode ?? "",
            analysisDepth ?? "Quick",
            configuration["Analysis:Provider"] ?? "Configured",
            configuration["Analysis:ProviderOrder"] ?? "Gemini,Groq",
            configuration["GEMINI_MODEL"] ?? configuration["AI:Providers:Gemini:Model"] ?? "gemini-3.1-flash-lite",
            configuration["GROQ_MODEL"] ?? configuration["AI:Providers:Groq:Model"] ?? "openai/gpt-oss-120b",
            allowCloudProcessing ? "cloud-consent" : "local-only");
        input += "\0" + string.Join(
            '\0',
            configuration["Analysis:ChunkCharacters"] ?? "20000",
            configuration["Analysis:ChunkOverlapLines"] ?? "0",
            configuration["Analysis:MaxChunks"] ?? "40",
            configuration["Analysis:MaxParallelCalls"] ?? "1",
            configuration["Analysis:MaxGlobalConcurrentCalls"] ?? "3",
            configuration["Analysis:ModelContextTokens"] ?? "32768",
            configuration["AI:MaxOutputTokens"] ?? "8192",
            configuration["AI:MaxInferenceCallsPerRequest"] ?? "500",
            configuration["AI:FreeTierAnalysisMode"] ?? "false",
            "analysis-prompt-v2");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
