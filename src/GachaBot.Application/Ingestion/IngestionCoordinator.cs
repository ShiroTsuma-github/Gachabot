namespace GachaBot.Application.Ingestion;

public sealed class IngestionCoordinator(
    ISourceStateStore sourceStateStore,
    IIngestionSink sink,
    TimeProvider? timeProvider = null)
{
    private const int BatchSize = 50;

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<IngestionResult> RunAsync(
        IGameContentSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Trust == SourceTrust.Disabled)
        {
            return IngestionResult.Empty;
        }

        try
        {
            var hasBaseline = await sourceStateStore
                .HasCompletedBaselineAsync(source.Key, cancellationToken)
                .ConfigureAwait(false);
            // A timeline reports both upcoming and already-active events. Keep its
            // start-aware behaviour on every poll: future items are scheduled,
            // while newly discovered active items are due immediately.
            var disposition = source.SchedulesUpcomingEvents
                ? PublicationDisposition.ScheduleUpcoming
                : SelectDisposition(source.Trust, hasBaseline);
            var seen = 0;
            var created = 0;
            var updated = 0;
            var batch = new List<SourceContentSnapshot>(BatchSize);

            await foreach (var snapshot in source.FetchAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                batch.Add(snapshot);
                if (batch.Count == BatchSize)
                {
                    var counts = await FlushAsync(batch, disposition, cancellationToken).ConfigureAwait(false);
                    seen += counts.Seen;
                    created += counts.Created;
                    updated += counts.Updated;
                }
            }

            if (batch.Count > 0)
            {
                var counts = await FlushAsync(batch, disposition, cancellationToken).ConfigureAwait(false);
                seen += counts.Seen;
                created += counts.Created;
                updated += counts.Updated;
            }

            await sourceStateStore
                .MarkSucceededAsync(source.Key, _timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            return new IngestionResult(seen, created, updated, seen - created - updated);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await sourceStateStore.MarkFailedAsync(
                source.Key,
                _timeProvider.GetUtcNow(),
                exception.Message,
                cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<(int Seen, int Created, int Updated)> FlushAsync(
        List<SourceContentSnapshot> batch,
        PublicationDisposition disposition,
        CancellationToken cancellationToken)
    {
        var outcomes = await sink.UpsertBatchAsync(batch, disposition, cancellationToken)
            .ConfigureAwait(false);
        if (outcomes.Count != batch.Count)
        {
            throw new InvalidOperationException("The ingestion sink returned an invalid batch result count.");
        }

        var result = (
            outcomes.Count,
            outcomes.Count(outcome => outcome == ContentUpsertOutcome.Created),
            outcomes.Count(outcome => outcome == ContentUpsertOutcome.Updated));
        batch.Clear();
        return result;
    }

    private static PublicationDisposition SelectDisposition(SourceTrust trust, bool hasBaseline) =>
        trust == SourceTrust.ReviewRequired
            ? PublicationDisposition.AwaitReview
            : !hasBaseline
            ? PublicationDisposition.SuppressBaseline
            : trust switch
            {
                SourceTrust.Official or SourceTrust.Trusted => PublicationDisposition.AutoPublish,
                SourceTrust.ReviewRequired => PublicationDisposition.AwaitReview,
                _ => PublicationDisposition.AwaitReview,
            };
}

public sealed record IngestionResult(int Seen, int Created, int Updated, int Unchanged)
{
    public static IngestionResult Empty { get; } = new(0, 0, 0, 0);
}
