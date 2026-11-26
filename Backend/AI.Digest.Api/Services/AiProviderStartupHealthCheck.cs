using System.Diagnostics;

namespace AI.Digest.Api.Services;

public sealed class AiProviderStartupHealthCheck(
    IServiceScopeFactory scopeFactory,
    AiProviderConfiguration configuration,
    AiProviderHealthTracker healthTracker,
    ILogger<AiProviderStartupHealthCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var clients = scope.ServiceProvider.GetServices<IAiModelClient>()
            .Where(client =>
                client.IsConfigured &&
                configuration.IsEnabledFromConfiguration(client.ProviderId) &&
                configuration.HasSupportedModel(client.ProviderId))
            .OrderBy(client => configuration.Priority(client.ProviderId));

        foreach (var client in clients)
        {
            stoppingToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(
                Math.Min(configuration.ProviderTimeoutSeconds, 10)));
            var timer = Stopwatch.StartNew();

            try
            {
                if (client is not IAiProviderConnectionTester connectionTester)
                    continue;

                await connectionTester.TestConnectionAsync(timeout.Token);
                healthTracker.RecordSuccess(client.ProviderId, timer.Elapsed);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                RecordFailure(client.ProviderId, AnalysisFailureCode.RequestTimeout);
                logger.LogWarning(
                    "AI provider {ProviderId} startup check timed out.",
                    client.ProviderId);
            }
            catch (AnalysisProviderException exception)
            {
                var failureCode = exception.FailureCode;
                RecordFailure(client.ProviderId, failureCode);
                logger.LogWarning(
                    "AI provider {ProviderId} startup check failed with {FailureCode}.",
                    client.ProviderId,
                    failureCode);
            }
        }
    }

    private void RecordFailure(string providerId, AnalysisFailureCode? failureCode) =>
        healthTracker.RecordFailure(
            providerId,
            configuration.CircuitFailureThreshold,
            configuration.CircuitOpenDuration,
            DateTimeOffset.UtcNow,
            failureCode);
}
