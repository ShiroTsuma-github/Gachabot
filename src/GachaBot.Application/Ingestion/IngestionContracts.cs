using GachaBot.Domain.Content;
using GachaBot.Domain.Games;

namespace GachaBot.Application.Ingestion;

public enum SourceTrust
{
    Official = 1,
    Trusted = 2,
    ReviewRequired = 3,
    Disabled = 4,
}

public enum PublicationDisposition
{
    SuppressBaseline = 1,
    AutoPublish = 2,
    AwaitReview = 3,
    ScheduleUpcoming = 4,
}

public enum ContentUpsertOutcome
{
    Created = 1,
    Updated = 2,
    Unchanged = 3,
}

public sealed record SourceContentSnapshot(
    string SourceKey,
    string ExternalId,
    GameKey Game,
    ContentKind Kind,
    string Title,
    Uri SourceUrl,
    ContentDocument Document,
    DateTimeOffset? PublishedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool ReplacesSourceItems = false,
    DateTimeOffset? PublishAtUtc = null)
{
    public string Identity => $"{SourceKey}:{ExternalId}";
}

public interface IGameContentSource
{
    string Key { get; }

    SourceTrust Trust { get; }

    bool SchedulesUpcomingEvents => false;

    IAsyncEnumerable<SourceContentSnapshot> FetchAsync(CancellationToken cancellationToken);
}

public interface ISourceStateStore
{
    Task<bool> HasCompletedBaselineAsync(string sourceKey, CancellationToken cancellationToken);

    Task MarkSucceededAsync(
        string sourceKey,
        DateTimeOffset completedAtUtc,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        string sourceKey,
        DateTimeOffset attemptedAtUtc,
        string failureMessage,
        CancellationToken cancellationToken);
}

public sealed record SourceStateSnapshot(
    string SourceKey,
    bool HasCompletedBaseline,
    DateTimeOffset? LastSuccessfulRunUtc,
    DateTimeOffset? LastAttemptUtc,
    string? LastFailureMessage);

public interface ISourceStateQuery
{
    Task<SourceStateSnapshot?> GetSourceStateAsync(
        string sourceKey,
        CancellationToken cancellationToken);
}

public interface IIngestionSink
{
    Task<ContentUpsertOutcome> UpsertAsync(
        SourceContentSnapshot snapshot,
        PublicationDisposition disposition,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<ContentUpsertOutcome>> UpsertBatchAsync(
        IReadOnlyList<SourceContentSnapshot> snapshots,
        PublicationDisposition disposition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        var outcomes = new ContentUpsertOutcome[snapshots.Count];
        for (var index = 0; index < snapshots.Count; index++)
        {
            outcomes[index] = await UpsertAsync(snapshots[index], disposition, cancellationToken)
                .ConfigureAwait(false);
        }

        return outcomes;
    }
}

public readonly record struct SourceContentState(bool Exists, bool NeedsRefresh);

public interface ISourceContentLookup
{
    Task<bool> ExistsAsync(
        string sourceKey,
        string externalId,
        CancellationToken cancellationToken);

    Task<bool> NeedsContentRefreshAsync(
        string sourceKey,
        string externalId,
        CancellationToken cancellationToken);

    async Task<IReadOnlyDictionary<string, SourceContentState>> GetContentStatesAsync(
        string sourceKey,
        IReadOnlyCollection<string> externalIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        var states = new Dictionary<string, SourceContentState>(StringComparer.Ordinal);
        foreach (var externalId in externalIds.Distinct(StringComparer.Ordinal))
        {
            if (!await ExistsAsync(sourceKey, externalId, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            states[externalId] = new SourceContentState(
                true,
                await NeedsContentRefreshAsync(sourceKey, externalId, cancellationToken).ConfigureAwait(false));
        }

        return states;
    }
}
