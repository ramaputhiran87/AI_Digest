using System.Security.Claims;
using AI.Digest.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AI.Digest.Api.Tests;

public sealed class CloudAiConsentTests
{
    [Fact]
    public void ConsentPersistsPerAccountAndCanBeWithdrawn()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ai-digest-consent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AI_CONSENT_STORE_PATH"] = Path.Combine(directory, "consents.bin")
                })
                .Build();
            var dataProtection = new EphemeralDataProtectionProvider();
            var store = new CloudAiConsentStore(dataProtection, configuration);
            var service = new CloudAiConsentService(store);
            var firstUser = CreateAuthenticatedContext("account-1");

            var granted = service.Grant(
                firstUser,
                new CloudAiConsentRequest(
                    true,
                    CloudAiConsentService.Scope,
                    CloudAiConsentService.PolicyVersion));

            Assert.True(granted.IsConsented);
            Assert.NotNull(granted.GrantedAt);
            Assert.True(service.HasValidConsent(
                CreateAuthenticatedContext("account-1"),
                CloudAiConsentService.Scope,
                CloudAiConsentService.PolicyVersion));
            Assert.False(service.HasValidConsent(
                CreateAuthenticatedContext("account-2"),
                CloudAiConsentService.Scope,
                CloudAiConsentService.PolicyVersion));
            Assert.False(service.HasValidConsent(
                CreateAuthenticatedContext("account-1"),
                "changed-scope",
                CloudAiConsentService.PolicyVersion));

            var reloaded = new CloudAiConsentService(
                new CloudAiConsentStore(dataProtection, configuration));
            Assert.True(reloaded.GetStatus(CreateAuthenticatedContext("account-1")).IsConsented);

            var withdrawn = reloaded.Withdraw(CreateAuthenticatedContext("account-1"));
            Assert.False(withdrawn.IsConsented);
            Assert.NotNull(withdrawn.WithdrawnAt);
            Assert.False(reloaded.HasValidConsent(
                CreateAuthenticatedContext("account-1"),
                CloudAiConsentService.Scope,
                CloudAiConsentService.PolicyVersion));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ConsentRejectsUnselectedOrStalePolicy()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ai-digest-consent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI_CONSENT_STORE_PATH"] = Path.Combine(directory, "consents.bin")
            })
            .Build();
        try
        {
            var service = new CloudAiConsentService(
                new CloudAiConsentStore(
                    new EphemeralDataProtectionProvider(),
                    configuration));

            Assert.Throws<ArgumentException>(() => service.Grant(
                CreateAuthenticatedContext("account-1"),
                new CloudAiConsentRequest(
                    false,
                    CloudAiConsentService.Scope,
                    CloudAiConsentService.PolicyVersion)));
            Assert.Throws<ArgumentException>(() => service.Grant(
                CreateAuthenticatedContext("account-1"),
                new CloudAiConsentRequest(true, CloudAiConsentService.Scope, "stale")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static DefaultHttpContext CreateAuthenticatedContext(string subject)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", subject) },
            authenticationType: "test"));
        return context;
    }
}
