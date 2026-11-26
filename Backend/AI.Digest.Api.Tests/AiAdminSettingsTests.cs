using System.Net;
using AI.Digest.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AI.Digest.Api.Tests;

public sealed class AiAdminSettingsTests
{
    [Fact]
    public void MissingEmptyAndPlaceholderKeysAreRejectedWithoutRejectingRealisticKeys()
    {
        Assert.False(AiAdminSettingsStore.IsUsableApiKey(null));
        Assert.False(AiAdminSettingsStore.IsUsableApiKey(""));
        Assert.False(AiAdminSettingsStore.IsUsableApiKey("your_gemini_api_key"));
        Assert.False(AiAdminSettingsStore.IsUsableApiKey("${GROQ_API_KEY}"));
        Assert.True(AiAdminSettingsStore.IsUsableApiKey("AIzaSyD-realistic-example-key"));
    }

    [Fact]
    public void SavedKeysAreEncryptedAndEnvironmentKeysTakePrecedence()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ai-digest-admin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.bin");
            var baseConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AI_SETTINGS_PATH"] = path
                })
                .Build();
            var dataProtection = new EphemeralDataProtectionProvider();
            var store = new AiAdminSettingsStore(dataProtection, baseConfiguration);
            store.Save(new AiAdminSettingsUpdate(
                "saved-gemini-api-key-example",
                "gemini-3.1-flash-lite",
                true,
                true,
                false,
                "saved-groq-api-key-example",
                "openai/gpt-oss-120b",
                true,
                false,
                true));

            var encrypted = File.ReadAllText(path);
            Assert.DoesNotContain("saved-gemini-api-key-example", encrypted, StringComparison.Ordinal);
            Assert.DoesNotContain("saved-groq-api-key-example", encrypted, StringComparison.Ordinal);

            var reloaded = new AiAdminSettingsStore(dataProtection, baseConfiguration);
            var adminStoredKey = reloaded.ResolveApiKey("Gemini", baseConfiguration);
            Assert.Equal("saved-gemini-api-key-example", adminStoredKey.ApiKey);
            Assert.Equal("Admin store", adminStoredKey.Source);

            var deploymentConfiguration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AI_SETTINGS_PATH"] = path,
                    ["GEMINI_API_KEY"] = "deployment-gemini-api-key"
                })
                .Build();
            var deploymentKey = reloaded.ResolveApiKey("Gemini", deploymentConfiguration);
            Assert.Equal("deployment-gemini-api-key", deploymentKey.ApiKey);
            Assert.Equal("Environment", deploymentKey.Source);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExistingHierarchicalGeminiConfigurationIsDetected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gemini:ApiKey"] = "existing-gemini-api-key"
            })
            .Build();
        var store = new AiAdminSettingsStore(
            new EphemeralDataProtectionProvider(),
            configuration);

        var resolved = store.ResolveApiKey("Gemini", configuration);

        Assert.True(AiAdminSettingsStore.IsUsableApiKey(resolved.ApiKey));
        Assert.Equal("existing-gemini-api-key", resolved.ApiKey);
        Assert.Equal("Configuration", resolved.Source);
    }

    [Fact]
    public void AdminTokenRequiresStrongSecretAndRejectsIncorrectTokens()
    {
        const string expectedToken = "a-long-random-administrator-token-value";
        var authorized = new DefaultHttpContext();
        authorized.Connection.RemoteIpAddress = IPAddress.Loopback;
        authorized.Request.Headers.Authorization = "Bearer " + expectedToken;

        Assert.Null(AiAdminAccess.Validate(authorized, expectedToken));
        Assert.NotNull(AiAdminAccess.Validate(authorized, "short"));

        authorized.Request.Headers.Authorization = "Bearer wrong-token";
        Assert.NotNull(AiAdminAccess.Validate(authorized, expectedToken));
    }

    [Fact]
    public void AdminBearerTokenOverRemoteHttpIsRejected()
    {
        const string expectedToken = "a-long-random-administrator-token-value";
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.8");
        context.Request.Headers.Authorization = "Bearer " + expectedToken;

        Assert.NotNull(AiAdminAccess.Validate(context, expectedToken));
    }
}
