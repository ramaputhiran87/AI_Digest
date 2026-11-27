namespace AI.Digest.Api.Services;

public sealed class AiInferenceConcurrencyGate(IConfiguration configuration)
{
    private readonly SemaphoreSlim semaphore = new(Math.Clamp(
        configuration.GetValue("Analysis:MaxGlobalConcurrentCalls", 3), 1, 3));
    private int activeCount;
    private int waitingCount;

    public int ActiveCount => Volatile.Read(ref activeCount);
    public int WaitingCount => Volatile.Read(ref waitingCount);

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref waitingCount);
        try
        {
            await semaphore.WaitAsync(cancellationToken);
            Interlocked.Increment(ref activeCount);
        }
        finally
        {
            Interlocked.Decrement(ref waitingCount);
        }
    }

    public void Release()
    {
        Interlocked.Decrement(ref activeCount);
        semaphore.Release();
    }
}
