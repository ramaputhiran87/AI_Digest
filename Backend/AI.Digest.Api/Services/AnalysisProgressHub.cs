using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AI.Digest.Api.Services;

public sealed record AnalysisProgressEvent(
    string Stage,
    int CompletedChunks = 0,
    int TotalChunks = 0,
    int CompletedInferenceCalls = 0,
    int? PlannedInferenceCalls = null,
    string? Provider = null,
    string? Model = null,
    string? Message = null,
    bool Completed = false,
    bool Cancelled = false,
    bool Failed = false,
    DateTimeOffset? OccurredAt = null);

public sealed class AnalysisProgressHub
{
    private readonly ConcurrentDictionary<Guid, Operation> operations = new();

    public void Start(Guid id)
    {
        var operation = operations.GetOrAdd(id, _ => new Operation());
        operation.MarkStarted();
        Report(id, new AnalysisProgressEvent(
            "Document received", OccurredAt: DateTimeOffset.UtcNow));
    }

    public void Report(Guid id, AnalysisProgressEvent progress)
    {
        var operation = operations.GetOrAdd(id, _ => new Operation());
        operation.Publish(progress with { OccurredAt = progress.OccurredAt ?? DateTimeOffset.UtcNow });
        if (progress.Completed || progress.Cancelled || progress.Failed)
            _ = RemoveAfterDelayAsync(id, operation);
    }

    public ProgressSubscription Subscribe(Guid id)
    {
        var operation = operations.GetOrAdd(id, _ => new Operation());
        _ = RemoveOrphanAfterDelayAsync(id, operation);
        return operation.Subscribe();
    }

    private async Task RemoveOrphanAfterDelayAsync(Guid id, Operation operation)
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        if (!operation.HasStarted &&
            operations.TryGetValue(id, out var current) &&
            ReferenceEquals(current, operation))
        {
            ((ICollection<KeyValuePair<Guid, Operation>>)operations)
                .Remove(new KeyValuePair<Guid, Operation>(id, operation));
        }
    }

    private async Task RemoveAfterDelayAsync(Guid id, Operation operation)
    {
        await Task.Delay(TimeSpan.FromMinutes(2));
        if (operations.TryGetValue(id, out var current) && ReferenceEquals(current, operation))
        {
            ((ICollection<KeyValuePair<Guid, Operation>>)operations)
                .Remove(new KeyValuePair<Guid, Operation>(id, operation));
        }
    }

    private sealed class Operation
    {
        private readonly object gate = new();
        private readonly List<AnalysisProgressEvent> history = [];
        private readonly List<Channel<AnalysisProgressEvent>> subscribers = [];
        private bool completed;
        private bool started;

        public bool HasStarted
        {
            get
            {
                lock (gate) return started;
            }
        }

        public void MarkStarted()
        {
            lock (gate) started = true;
        }

        public void Publish(AnalysisProgressEvent progress)
        {
            lock (gate)
            {
                if (completed) return;
                history.Add(progress);
                if (history.Count > 256)
                    history.RemoveRange(0, history.Count - 256);
                foreach (var subscriber in subscribers)
                    subscriber.Writer.TryWrite(progress);
                if (progress.Completed || progress.Cancelled || progress.Failed)
                {
                    completed = true;
                    foreach (var subscriber in subscribers)
                        subscriber.Writer.TryComplete();
                    subscribers.Clear();
                }
            }
        }

        public ProgressSubscription Subscribe()
        {
            lock (gate)
            {
                var channel = Channel.CreateUnbounded<AnalysisProgressEvent>(
                    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
                if (!completed)
                    subscribers.Add(channel);
                else
                    channel.Writer.TryComplete();
                return new ProgressSubscription(history.ToArray(), channel.Reader);
            }
        }
    }
}

public sealed record ProgressSubscription(
    IReadOnlyList<AnalysisProgressEvent> History,
    ChannelReader<AnalysisProgressEvent> Updates)
{
    public async IAsyncEnumerable<AnalysisProgressEvent> ReadUpdatesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var progress in Updates.ReadAllAsync(cancellationToken))
            yield return progress;
    }
}

public sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
