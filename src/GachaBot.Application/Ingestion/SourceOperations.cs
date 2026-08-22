namespace GachaBot.Application.Ingestion;

public sealed record SourceStatus(
    string Key,
    SourceTrust Trust,
    bool HasCompletedBaseline,
    DateTimeOffset? LastSuccessfulRunUtc,
    DateTimeOffset? LastAttemptUtc,
    string? LastFailureMessage);

public sealed record SourceRunResult(string Key, bool Succeeded, IngestionResult? Result, string? FailureMessage);

public interface ISourceOperations
{
    Task<IReadOnlyList<SourceStatus>> GetStatusesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SourceRunResult>> RunAllAsync(CancellationToken cancellationToken);

    Task<SourceRunResult> RunAsync(string sourceKey, CancellationToken cancellationToken);
}
