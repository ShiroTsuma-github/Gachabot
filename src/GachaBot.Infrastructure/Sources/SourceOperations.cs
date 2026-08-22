using System.Diagnostics;
using GachaBot.Application.Ingestion;
using GachaBot.Infrastructure.Database;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Sources;

public sealed partial class SourceOperations(
    IEnumerable<IGameContentSource> sources,
    ISourceStateQuery sourceStateQuery,
    IServiceScopeFactory scopeFactory,
    DatabaseInitializer databaseInitializer,
    ILogger<SourceOperations> logger) : ISourceOperations
{
    private readonly IGameContentSource[] _sources = sources.ToArray();

    public async Task<IReadOnlyList<SourceStatus>> GetStatusesAsync(CancellationToken cancellationToken)
    {
        var statuses = new List<SourceStatus>(_sources.Length);
        foreach (var source in _sources)
        {
            var state = await sourceStateQuery.GetSourceStateAsync(source.Key, cancellationToken)
                .ConfigureAwait(false);
            statuses.Add(state is not null
                ? new SourceStatus(
                    source.Key,
                    source.Trust,
                    state.HasCompletedBaseline,
                    state.LastSuccessfulRunUtc,
                    state.LastAttemptUtc,
                    state.LastFailureMessage)
                : new SourceStatus(source.Key, source.Trust, false, null, null, null));
        }

        return statuses.OrderBy(status => status.Key, StringComparer.Ordinal).ToArray();
    }

    public async Task<IReadOnlyList<SourceRunResult>> RunAllAsync(CancellationToken cancellationToken)
    {
        var results = new List<SourceRunResult>();
        foreach (var source in _sources)
        {
            results.Add(await RunAsync(source.Key, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    public async Task<SourceRunResult> RunAsync(
        string sourceKey,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        if (!_sources.Any(source => string.Equals(source.Key, sourceKey, StringComparison.Ordinal)))
        {
            throw new KeyNotFoundException($"Source '{sourceKey}' was not found.");
        }

        var startedAt = Stopwatch.GetTimestamp();
        LogSourceStarted(logger, sourceKey);
        try
        {
            await databaseInitializer.EnsureSourceAsync(sourceKey, cancellationToken).ConfigureAwait(false);
            await using var scope = scopeFactory.CreateAsyncScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<IngestionCoordinator>();
            var scopedSource = scope.ServiceProvider
                .GetServices<IGameContentSource>()
                .Single(source => string.Equals(source.Key, sourceKey, StringComparison.Ordinal));
            var result = await coordinator.RunAsync(scopedSource, cancellationToken).ConfigureAwait(false);
            var elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogSourceCompleted(
                logger,
                sourceKey,
                result.Seen,
                result.Created,
                result.Updated,
                result.Unchanged,
                elapsedMilliseconds);
            return new SourceRunResult(sourceKey, true, result, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
            LogSourceCanceled(
                logger,
                sourceKey,
                elapsedMilliseconds);
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSourceFailure(logger, sourceKey, exception);
            return new SourceRunResult(sourceKey, false, null, exception.Message);
        }
    }

    [LoggerMessage(
        EventId = 2002,
        Level = LogLevel.Information,
        Message = "Starting ingestion for source {SourceKey}.")]
    private static partial void LogSourceStarted(ILogger logger, string sourceKey);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Information,
        Message = "Completed ingestion for source {SourceKey}: {Seen} seen, {Created} created, {Updated} updated, {Unchanged} unchanged in {ElapsedMilliseconds} ms.")]
    private static partial void LogSourceCompleted(
        ILogger logger,
        string sourceKey,
        int seen,
        int created,
        int updated,
        int unchanged,
        long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 2004,
        Level = LogLevel.Warning,
        Message = "Ingestion for source {SourceKey} was canceled after {ElapsedMilliseconds} ms.")]
    private static partial void LogSourceCanceled(
        ILogger logger,
        string sourceKey,
        long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "Source {SourceKey} ingestion failed.")]
    private static partial void LogSourceFailure(ILogger logger, string sourceKey, Exception exception);
}
