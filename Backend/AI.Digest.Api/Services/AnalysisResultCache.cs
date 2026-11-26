using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AI.Digest.Api.Models;
using Microsoft.Extensions.Caching.Memory;

namespace AI.Digest.Api.Services;

public sealed class AnalysisResultCache(
    IMemoryCache cache,
    IConfiguration configuration)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<DigestResult>>> inFlight =
        new(StringComparer.Ordinal);

    public async Task<DigestResult> GetOrCreateAsync(
        string userScope,
        string title,
        string content,
        string? useCase,
        string? analysisMode,
        bool allowCloudProcessing,
        Func<Task<DigestResult>> create)
    {
        var key = BuildKey(
            userScope, title, content, useCase, analysisMode, allowCloudProcessing);
        if (cache.TryGetValue(key, out DigestResult? cached) && cached is not null)
            return cached;

        var created = new Lazy<Task<DigestResult>>(
            create, LazyThreadSafetyMode.ExecutionAndPublication);
        var operation = inFlight.GetOrAdd(key, created);
        try
        {
            var result = await operation.Value;
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
            ((ICollection<KeyValuePair<string, Lazy<Task<DigestResult>>>>)inFlight)
                .Remove(new KeyValuePair<string, Lazy<Task<DigestResult>>>(key, operation));
        }
    }

    private static string BuildKey(
        string userScope,
        string title,
        string content,
        string? useCase,
        string? analysisMode,
        bool allowCloudProcessing)
    {
        var input = string.Join(
            '\0',
            userScope,
            title,
            content,
            useCase ?? "",
            analysisMode ?? "",
            allowCloudProcessing ? "cloud-consent" : "local-only");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}
