using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace AI.Digest.Api.Services;

public sealed record AiAdminSettingsUpdate(
    string? GeminiApiKey,
    string? GeminiModel,
    bool GeminiEnabled,
    bool GeminiFreeTierConfirmed,
    bool ClearGeminiApiKey,
    string? GroqApiKey,
    string? GroqModel,
    bool GroqEnabled,
    bool ClearGroqApiKey,
    bool AutomaticFallback);

public sealed class AiAdminSettingsStore
{
    private const string GeminiModelName = "gemini-3.1-flash-lite";
    private const string GroqModelName = "openai/gpt-oss-120b";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly object gate = new();
    private readonly IDataProtector protector;
    private readonly string settingsPath;
    private StoredSettings settings;

    public AiAdminSettingsStore(
        IDataProtectionProvider dataProtectionProvider,
        IConfiguration configuration)
    {
        protector = dataProtectionProvider.CreateProtector("AI.Digest.AiAdminSettings.v1");
        var configuredPath = configuration["AI_SETTINGS_PATH"];
        settingsPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(configuredPath)
                ? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AI.Digest",
                    "ai-provider-settings.bin")
                : configuredPath);
        settings = Load();
    }

    public AiApiKeyResolution ResolveApiKey(string providerId, IConfiguration configuration)
    {
        var envName = $"{providerId.ToUpperInvariant()}_API_KEY";
        var environmentKey = configuration[envName];
        if (IsUsableApiKey(environmentKey))
            return new AiApiKeyResolution(environmentKey!.Trim(), "Environment");

        var configuredKey = configuration[$"{providerId}:ApiKey"];
        if (IsUsableApiKey(configuredKey))
            return new AiApiKeyResolution(configuredKey!.Trim(), "Configuration");

        lock (gate)
        {
            var storedKey = GetStoredApiKey(providerId, settings);
            return IsUsableApiKey(storedKey)
                ? new AiApiKeyResolution(storedKey!.Trim(), "Admin store")
                : new AiApiKeyResolution(null, GetMissingSource(environmentKey, configuredKey));
        }
    }

    public bool? GetEnabled(string providerId)
    {
        lock (gate)
        {
            return providerId.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
                ? settings.GeminiEnabled
                : providerId.Equals("Groq", StringComparison.OrdinalIgnoreCase)
                    ? settings.GroqEnabled
                    : null;
        }
    }

    public string? GetModel(string providerId)
    {
        lock (gate)
        {
            return providerId.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
                ? settings.GeminiModel
                : providerId.Equals("Groq", StringComparison.OrdinalIgnoreCase)
                    ? settings.GroqModel
                    : null;
        }
    }

    public bool? GetFallbackEnabled()
    {
        lock (gate)
            return settings.AutomaticFallback;
    }

    public bool? GetGeminiFreeTierConfirmed()
    {
        lock (gate)
            return settings.GeminiFreeTierConfirmed;
    }

    public void Save(AiAdminSettingsUpdate update)
    {
        if (!string.Equals(update.GeminiModel, GeminiModelName, StringComparison.Ordinal) ||
            !string.Equals(update.GroqModel, GroqModelName, StringComparison.Ordinal))
        {
            throw new ArgumentException("Only the configured Gemini and Groq models are supported.");
        }
        if ((!string.IsNullOrWhiteSpace(update.GeminiApiKey) &&
             !IsUsableApiKey(update.GeminiApiKey)) ||
            (!string.IsNullOrWhiteSpace(update.GroqApiKey) &&
             !IsUsableApiKey(update.GroqApiKey)))
        {
            throw new ArgumentException("An API key is empty or appears to be a placeholder.");
        }

        lock (gate)
        {
            var updated = settings with
            {
                GeminiApiKey = update.ClearGeminiApiKey
                    ? null
                    : string.IsNullOrWhiteSpace(update.GeminiApiKey)
                        ? settings.GeminiApiKey
                        : update.GeminiApiKey.Trim(),
                GeminiModel = GeminiModelName,
                GeminiEnabled = update.GeminiEnabled,
                GeminiFreeTierConfirmed = update.GeminiFreeTierConfirmed,
                GroqApiKey = update.ClearGroqApiKey
                    ? null
                    : string.IsNullOrWhiteSpace(update.GroqApiKey)
                        ? settings.GroqApiKey
                        : update.GroqApiKey.Trim(),
                GroqModel = GroqModelName,
                GroqEnabled = update.GroqEnabled,
                AutomaticFallback = update.AutomaticFallback
            };
            Persist(updated);
            settings = updated;
        }
    }

    public static bool IsUsableApiKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var key = value.Trim();
        return key.Length >= 8 &&
               !key.StartsWith("${", StringComparison.Ordinal) &&
               !key.StartsWith('<') &&
               !key.Contains("your_", StringComparison.OrdinalIgnoreCase) &&
               !key.Contains("your-", StringComparison.OrdinalIgnoreCase) &&
               !key.Contains("replace", StringComparison.OrdinalIgnoreCase) &&
               !key.Contains("placeholder", StringComparison.OrdinalIgnoreCase) &&
               !key.Equals("changeme", StringComparison.OrdinalIgnoreCase);
    }

    private StoredSettings Load()
    {
        if (!File.Exists(settingsPath))
            return new StoredSettings();

        try
        {
            var protectedJson = File.ReadAllText(settingsPath);
            var json = protector.Unprotect(protectedJson);
            return JsonSerializer.Deserialize<StoredSettings>(json, JsonOptions)
                   ?? throw new InvalidDataException();
        }
        catch (Exception exception) when (
            exception is CryptographicException or JsonException or InvalidDataException)
        {
            throw new InvalidOperationException(
                "Saved AI settings could not be decrypted or parsed. Check the backend data-protection key ring.",
                exception);
        }
    }

    private void Persist(StoredSettings updated)
    {
        var directory = Path.GetDirectoryName(settingsPath)
                        ?? throw new InvalidOperationException("The AI settings path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(updated, JsonOptions);
            var protectedJson = protector.Protect(json);
            File.WriteAllText(temporaryPath, protectedJson);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporaryPath, settingsPath, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(settingsPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string? GetStoredApiKey(string providerId, StoredSettings value) =>
        providerId.Equals("Gemini", StringComparison.OrdinalIgnoreCase)
            ? value.GeminiApiKey
            : providerId.Equals("Groq", StringComparison.OrdinalIgnoreCase)
                ? value.GroqApiKey
                : null;

    private static string GetMissingSource(string? environmentKey, string? configuredKey) =>
        !string.IsNullOrWhiteSpace(environmentKey)
            ? "Invalid environment value"
            : !string.IsNullOrWhiteSpace(configuredKey)
                ? "Invalid configuration value"
                : "Missing";

    private sealed record StoredSettings
    {
        public string? GeminiApiKey { get; init; }
        public string? GeminiModel { get; init; }
        public bool? GeminiEnabled { get; init; }
        public bool? GeminiFreeTierConfirmed { get; init; }
        public string? GroqApiKey { get; init; }
        public string? GroqModel { get; init; }
        public bool? GroqEnabled { get; init; }
        public bool? AutomaticFallback { get; init; }
    }
}

public sealed record AiApiKeyResolution(string? ApiKey, string Source);
