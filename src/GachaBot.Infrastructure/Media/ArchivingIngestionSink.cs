using System.Diagnostics;
using GachaBot.Application.Ingestion;
using GachaBot.Application.Media;
using GachaBot.Domain.Content;
using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Media;

public sealed partial class ArchivingIngestionSink(
    Database.CompositeContentStore inner,
    IMediaArchive mediaArchive,
    MediaAssetRegistry mediaAssetRegistry,
    ILogger<ArchivingIngestionSink> logger) : IIngestionSink
{
    public async Task<ContentUpsertOutcome> UpsertAsync(
        SourceContentSnapshot snapshot,
        PublicationDisposition disposition,
        CancellationToken cancellationToken)
    {
        var outcome = await inner.UpsertAsync(snapshot, disposition, cancellationToken).ConfigureAwait(false);
        await ArchiveChangedMediaAsync(snapshot, outcome, cancellationToken).ConfigureAwait(false);
        return outcome;
    }

    public async Task<IReadOnlyList<ContentUpsertOutcome>> UpsertBatchAsync(
        IReadOnlyList<SourceContentSnapshot> snapshots,
        PublicationDisposition disposition,
        CancellationToken cancellationToken)
    {
        var sourceKey = snapshots.Count == 0 ? "unknown" : snapshots[0].SourceKey;
        LogBatchStarted(logger, sourceKey, snapshots.Count);
        var outcomes = await inner.UpsertBatchAsync(snapshots, disposition, cancellationToken)
            .ConfigureAwait(false);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var created = outcomes.Count(outcome => outcome == ContentUpsertOutcome.Created);
            var updated = outcomes.Count(outcome => outcome == ContentUpsertOutcome.Updated);
            var unchanged = outcomes.Count(outcome => outcome == ContentUpsertOutcome.Unchanged);
            LogBatchStored(logger, sourceKey, outcomes.Count, created, updated, unchanged);
        }
        for (var index = 0; index < snapshots.Count; index++)
        {
            await ArchiveChangedMediaAsync(snapshots[index], outcomes[index], cancellationToken)
                .ConfigureAwait(false);
        }

        return outcomes;
    }

    private async Task ArchiveChangedMediaAsync(
        SourceContentSnapshot snapshot,
        ContentUpsertOutcome outcome,
        CancellationToken cancellationToken)
    {
        if (outcome == ContentUpsertOutcome.Unchanged)
        {
            return;
        }

        var mediaUrls = snapshot.Document.Blocks.SelectMany(block => block switch
        {
            ImageBlock image => [image.Url],
            GalleryBlock gallery => gallery.Images.Select(image => image.Url),
            _ => [],
        }).Distinct().ToArray();
        var mediaRequest = new MediaArchiveRequest(
            snapshot.SourceKey,
            snapshot.ExternalId,
            snapshot.Identity,
            snapshot.SourceUrl);
        await mediaAssetRegistry.RemoveAbsentAsync(mediaRequest, mediaUrls, cancellationToken)
            .ConfigureAwait(false);
        for (var index = 0; index < mediaUrls.Length; index++)
        {
            var mediaUrl = mediaUrls[index];
            try
            {
                LogMediaArchiveStarted(
                    logger,
                    snapshot.Identity,
                    index + 1,
                    mediaUrls.Length,
                    mediaUrl);
                var startedAt = Stopwatch.GetTimestamp();
                var archiveRequest = new MediaArchiveRequest(
                    snapshot.SourceKey,
                    snapshot.ExternalId,
                    snapshot.Identity,
                    mediaUrl);
                var archived = await mediaArchive
                    .ArchiveAsync(archiveRequest, cancellationToken)
                    .ConfigureAwait(false);
                await mediaAssetRegistry.RecordAsync(archiveRequest, archived, cancellationToken)
                    .ConfigureAwait(false);
                var elapsedMilliseconds = (long)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
                LogMediaArchiveCompleted(
                    logger,
                    snapshot.Identity,
                    index + 1,
                    mediaUrls.Length,
                    archived.Length,
                    archived.OriginalLength,
                    archived.WasCompressed,
                    archived.State,
                    elapsedMilliseconds);
            }
            catch (UnsupportedMediaTypeException exception)
            {
                LogUnsupportedMedia(logger, snapshot.Identity, mediaUrl, exception.ContentType);
            }
            catch (MediaSizeLimitExceededException exception)
            {
                LogOversizedMedia(
                    logger,
                    snapshot.Identity,
                    mediaUrl,
                    exception.ActualBytes,
                    exception.MaximumBytes);
            }
            catch (MediaCompressionException exception)
            {
                LogCompressionFailure(logger, snapshot.Identity, mediaUrl, exception);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogArchiveFailure(logger, snapshot.Identity, mediaUrl, exception);
            }
        }

    }

    [LoggerMessage(
        EventId = 3004,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: storing ingestion batch with {BatchSize} items.")]
    private static partial void LogBatchStarted(ILogger logger, string sourceKey, int batchSize);

    [LoggerMessage(
        EventId = 3005,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: stored {BatchSize} items ({Created} created, {Updated} updated, {Unchanged} unchanged); starting media archive.")]
    private static partial void LogBatchStored(
        ILogger logger,
        string sourceKey,
        int batchSize,
        int created,
        int updated,
        int unchanged);

    [LoggerMessage(
        EventId = 3006,
        Level = LogLevel.Information,
        Message = "Archiving media {MediaIndex}/{MediaCount} for {ContentIdentity} from {MediaUrl}.")]
    private static partial void LogMediaArchiveStarted(
        ILogger logger,
        string contentIdentity,
        int mediaIndex,
        int mediaCount,
        Uri mediaUrl);

    [LoggerMessage(
        EventId = 3007,
        Level = LogLevel.Information,
        Message = "Archived media {MediaIndex}/{MediaCount} for {ContentIdentity}: {LengthBytes} bytes from {OriginalLengthBytes} bytes (compressed: {WasCompressed}, state: {State}) in {ElapsedMilliseconds} ms.")]
    private static partial void LogMediaArchiveCompleted(
        ILogger logger,
        string contentIdentity,
        int mediaIndex,
        int mediaCount,
        long lengthBytes,
        long originalLengthBytes,
        bool wasCompressed,
        MediaArchiveState state,
        long elapsedMilliseconds);

    [LoggerMessage(
        EventId = 3008,
        Level = LogLevel.Warning,
        Message = "Could not produce a Discord-safe archived image for {ContentIdentity} from {MediaUrl}.")]
    private static partial void LogCompressionFailure(
        ILogger logger,
        string contentIdentity,
        Uri mediaUrl,
        Exception exception);

    [LoggerMessage(
        EventId = 3001,
        Level = LogLevel.Warning,
        Message = "Could not archive media for {ContentIdentity} from {MediaUrl}.")]
    private static partial void LogArchiveFailure(
        ILogger logger,
        string contentIdentity,
        Uri mediaUrl,
        Exception exception);

    [LoggerMessage(
        EventId = 3002,
        Level = LogLevel.Debug,
        Message = "Skipped non-image media candidate for {ContentIdentity} from {MediaUrl}; content type was '{ContentType}'.")]
    private static partial void LogUnsupportedMedia(
        ILogger logger,
        string contentIdentity,
        Uri mediaUrl,
        string contentType);

    [LoggerMessage(
        EventId = 3003,
        Level = LogLevel.Information,
        Message = "Skipped oversized media for {ContentIdentity} from {MediaUrl}; size was {ActualBytes} bytes and the configured limit is {MaximumBytes} bytes.")]
    private static partial void LogOversizedMedia(
        ILogger logger,
        string contentIdentity,
        Uri mediaUrl,
        long actualBytes,
        long maximumBytes);
}
