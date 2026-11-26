using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace AI.Digest.Api.Services;

public sealed record CloudAiConsentRequest(
    bool Consented,
    string Scope,
    string PolicyVersion);

public sealed record CloudAiConsentStatus(
    bool IsConsented,
    DateTimeOffset? GrantedAt,
    string Scope,
    string PolicyVersion,
    DateTimeOffset? WithdrawnAt);

public sealed record CloudAiConsentRecord(
    bool IsGranted,
    DateTimeOffset? GrantedAt,
    string Scope,
    string PolicyVersion,
    DateTimeOffset? WithdrawnAt);

public sealed class CloudAiConsentPersistenceException(
    string message,
    Exception innerException) : IOException(message, innerException)
{
}

public sealed class CloudAiConsentStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly object gate = new();
    private readonly IDataProtector protector;
    private readonly string storePath;
    private Dictionary<string, CloudAiConsentRecord> records;

    public CloudAiConsentStore(
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration)
    {
        protector = dataProtectionProvider.CreateProtector("AI.Digest.CloudAiConsent.v1");
        var configuredPath = configuration["AI_CONSENT_STORE_PATH"];
        storePath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AI.Digest",
                    "cloud-ai-consents.bin")
                : configuredPath);
        records = Load();
    }

    public CloudAiConsentRecord? Get(string subjectHash)
    {
        lock (gate)
            return records.TryGetValue(subjectHash, out var record) ? record : null;
    }

    public CloudAiConsentRecord Grant(string subjectHash, string scope, string policyVersion)
    {
        lock (gate)
        {
            var record = new CloudAiConsentRecord(
                true,
                DateTimeOffset.UtcNow,
                scope,
                policyVersion,
                null);
            var updated = new Dictionary<string, CloudAiConsentRecord>(records)
            {
                [subjectHash] = record
            };
            Persist(updated);
            records = updated;
            return record;
        }
    }

    public CloudAiConsentRecord? Withdraw(string subjectHash)
    {
        lock (gate)
        {
            if (!records.TryGetValue(subjectHash, out var record) || !record.IsGranted)
                return record;

            var withdrawn = record with
            {
                IsGranted = false,
                WithdrawnAt = DateTimeOffset.UtcNow
            };
            var updated = new Dictionary<string, CloudAiConsentRecord>(records)
            {
                [subjectHash] = withdrawn
            };
            Persist(updated);
            records = updated;
            return withdrawn;
        }
    }

    private Dictionary<string, CloudAiConsentRecord> Load()
    {
        if (!File.Exists(storePath))
            return new Dictionary<string, CloudAiConsentRecord>(StringComparer.Ordinal);

        try
        {
            var protectedJson = File.ReadAllText(storePath);
            var json = protector.Unprotect(protectedJson);
            return JsonSerializer.Deserialize<Dictionary<string, CloudAiConsentRecord>>(
                       json,
                       JsonOptions)
                   ?? throw new InvalidDataException();
        }
        catch (Exception exception) when (
            exception is CryptographicException or JsonException or InvalidDataException)
        {
            throw new InvalidOperationException(
                "Saved cloud AI consent data could not be decrypted or parsed. Check the backend data-protection key ring.",
                exception);
        }
    }

    private void Persist(Dictionary<string, CloudAiConsentRecord> updated)
    {
        var directory = Path.GetDirectoryName(storePath)
                        ?? throw new InvalidOperationException(
                            "The cloud AI consent store path is invalid.");
        var temporaryPath = storePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(updated, JsonOptions);
            var protectedJson = protector.Protect(json);
            File.WriteAllText(temporaryPath, protectedJson);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            File.Move(temporaryPath, storePath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException or
                NotSupportedException)
        {
            throw new CloudAiConsentPersistenceException(
                "Cloud AI consent could not be persisted.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}

public sealed class CloudAiConsentService(CloudAiConsentStore store)
{
    public const string Scope = "gemini-and-groq-cloud-document-analysis-and-presentation";
    public const string PolicyVersion = "1";
    public const string ProfileCookieName = "ai-digest-consent-profile";

    public CloudAiConsentStatus GetStatus(HttpContext context)
    {
        var subjectHash = GetSubjectHash(context, createLocalProfile: true);
        return ToStatus(store.Get(subjectHash));
    }

    public CloudAiConsentStatus Grant(
        HttpContext context,
        CloudAiConsentRequest request)
    {
        if (!request.Consented ||
            !string.Equals(request.Scope, Scope, StringComparison.Ordinal) ||
            !string.Equals(request.PolicyVersion, PolicyVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("The cloud AI consent request does not match the current policy.");
        }

        var subjectHash = GetSubjectHash(context, createLocalProfile: true);
        return ToStatus(store.Grant(subjectHash, Scope, PolicyVersion));
    }

    public CloudAiConsentStatus Withdraw(HttpContext context)
    {
        var subjectHash = GetSubjectHash(context, createLocalProfile: true);
        return ToStatus(store.Withdraw(subjectHash));
    }

    public bool HasValidConsent(
        HttpContext context,
        string? scope,
        string? policyVersion)
    {
        if (!string.Equals(scope, Scope, StringComparison.Ordinal) ||
            !string.Equals(policyVersion, PolicyVersion, StringComparison.Ordinal))
        {
            return false;
        }

        var subjectHash = GetSubjectHash(context, createLocalProfile: true);
        var record = store.Get(subjectHash);
        return record is
        {
            IsGranted: true,
            Scope: Scope,
            PolicyVersion: PolicyVersion,
            WithdrawnAt: null
        };
    }

    private static CloudAiConsentStatus ToStatus(CloudAiConsentRecord? record)
    {
        var valid = record is
        {
            IsGranted: true,
            Scope: Scope,
            PolicyVersion: PolicyVersion,
            WithdrawnAt: null
        };
        return new CloudAiConsentStatus(
            valid,
            record?.GrantedAt,
            record?.Scope ?? Scope,
            record?.PolicyVersion ?? PolicyVersion,
            record?.WithdrawnAt);
    }

    private static string GetSubjectHash(HttpContext context, bool createLocalProfile)
    {
        var identity = context.User.Identity;
        string subject;
        if (identity?.IsAuthenticated == true)
        {
            var userId = context.User.FindFirst("oid")?.Value ??
                         context.User.FindFirst("sub")?.Value ??
                         context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                throw new UnauthorizedAccessException(
                    "Authenticated users must have a stable account identifier for cloud AI consent.");

            var tenantId = context.User.FindFirst("tid")?.Value ??
                           context.User.FindFirst("iss")?.Value ??
                           "";
            subject = $"account:{tenantId}:{userId}";
        }
        else
        {
            if (context.Connection.RemoteIpAddress is not { } address ||
                !System.Net.IPAddress.IsLoopback(address))
            {
                throw new UnauthorizedAccessException(
                    "Cloud AI consent is available only to authenticated users.");
            }

            var profileId = context.Request.Cookies[ProfileCookieName];
            if (!IsValidProfileId(profileId))
            {
                if (!createLocalProfile)
                    throw new UnauthorizedAccessException(
                        "A local browser profile is required for cloud AI consent.");

                profileId = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                context.Response.Cookies.Append(
                    ProfileCookieName,
                    profileId,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        Secure = context.Request.IsHttps,
                        SameSite = SameSiteMode.Strict,
                        IsEssential = true,
                        Path = "/",
                        MaxAge = TimeSpan.FromDays(365)
                    });
            }

            subject = $"local-profile:{profileId}";
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subject)));
    }

    private static bool IsValidProfileId(string? profileId) =>
        profileId is { Length: 64 } && profileId.All(Uri.IsHexDigit);
}
