using System.Security.Cryptography;
using System.Text;
using GachaBot.Application.Media;
using GachaBot.Domain.Content;
using GachaBot.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Media;

public sealed record MediaArchiveMigrationResult(
    int ContentItems,
    int ArchivedContentItems,
    int MediaUrls,
    int Migrated,
    int AlreadyCurrent,
    int Failed,
    int DeletedLegacyDirectories);

public sealed partial class MediaArchiveMigration(
    ISourceDatabaseFactory databaseFactory,
    IMediaArchive mediaArchive,
    MediaArchiveCatalog catalog,
    IMediaObjectStore objectStore,
    Microsoft.Extensions.Options.IOptions<S3MediaOptions> s3Options,
    MediaAssetRegistry mediaAssetRegistry,
    ILogger<MediaArchiveMigration> logger)
{
    public async Task<MediaArchiveMigrationResult> RunAsync(
        bool apply,
        bool deleteLegacy,
        string? sourceKey,
        CancellationToken cancellationToken)
    {
        var contentCount = 0;
        var archivedCount = 0;
        var mediaCount = 0;
        var migrated = 0;
        var current = 0;
        var failed = 0;
        var deleted = 0;
        var databaseKeys = string.IsNullOrWhiteSpace(sourceKey)
            ? databaseFactory.DatabaseKeys
            : databaseFactory.DatabaseKeys.Contains(sourceKey, StringComparer.Ordinal)
                ? [sourceKey]
                : throw new InvalidOperationException($"Unknown source key '{sourceKey}'.");
        foreach (var databaseKey in databaseKeys)
        {
            await using var context = databaseFactory.CreateDbContext(databaseKey);
            var records = await context.ContentItems.AsNoTracking()
                .OrderBy(item => item.CreatedAtUtc)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                contentCount++;
                archivedCount += record.Status == ContentStatus.Archived ? 1 : 0;
                var urls = ExtractMediaUrls(record.DocumentJson);
                mediaCount += urls.Length;
                var itemSucceeded = true;
                LogItemStarted(logger, contentCount, record.Identity, record.Status, urls.Length, apply);
                foreach (var url in urls)
                {
                    var externalId = record.ExternalId ?? record.Id.ToString("N");
                    if (await mediaAssetRegistry.IsRecordedAsync(
                            record.SourceKey,
                            externalId,
                            url,
                            cancellationToken).ConfigureAwait(false))
                    {
                        current++;
                        continue;
                    }

                    var localArchive = await catalog.TryResolveAsync(
                            record.SourceKey,
                            externalId,
                            url,
                            cancellationToken).ConfigureAwait(false);

                    if (!apply)
                    {
                        continue;
                    }

                    try
                    {
                        var request = new MediaArchiveRequest(
                            record.SourceKey,
                            externalId,
                            record.Identity,
                            url);
                        var archived = localArchive is null
                            ? await mediaArchive.ArchiveAsync(request, cancellationToken).ConfigureAwait(false)
                            : await UploadLegacyAsync(localArchive, cancellationToken).ConfigureAwait(false);
                        await mediaAssetRegistry.RecordAsync(request, archived, cancellationToken)
                            .ConfigureAwait(false);
                        LogMediaStored(
                            logger,
                            record.Identity,
                            url,
                            archived.State,
                            archived.OriginalLength,
                            archived.Length);
                        migrated++;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        itemSucceeded = false;
                        failed++;
                        LogMediaFailed(logger, record.Identity, url, exception);
                    }
                }

                if (apply && deleteLegacy && urls.Length > 0 && itemSucceeded &&
                    DeleteLegacyDirectory(record.Identity))
                {
                    deleted++;
                }
            }
        }

        var result = new MediaArchiveMigrationResult(
            contentCount,
            archivedCount,
            mediaCount,
            migrated,
            current,
            failed,
            deleted);
        LogCompleted(
            logger,
            result.ContentItems,
            result.ArchivedContentItems,
            result.MediaUrls,
            result.Migrated,
            result.AlreadyCurrent,
            result.Failed,
            result.DeletedLegacyDirectories,
            apply);
        return result;
    }

    private async Task<ArchivedMedia> UploadLegacyAsync(
        ArchivedMediaAttachment attachment,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(attachment.FullPath, cancellationToken).ConfigureAwait(false);
        var sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var extension = Path.GetExtension(attachment.FullPath).ToLowerInvariant();
        var prefix = s3Options.Value.Prefix.Trim('/');
        var objectKey = string.IsNullOrWhiteSpace(prefix)
            ? $"{sha256}{extension}"
            : $"{prefix}/{sha256}{extension}";
        await objectStore.PutIfAbsentAsync(objectKey, bytes, attachment.ContentType, cancellationToken)
            .ConfigureAwait(false);
        return new ArchivedMedia(
            objectKey,
            attachment.ContentType,
            bytes.LongLength,
            sha256,
            bytes.LongLength,
            WasCompressed: false,
            State: MediaArchiveState.StoredOriginal,
            ProcessingNote: "Migrated from local media archive.",
            ObjectKey: objectKey);
    }

    private static Uri[] ExtractMediaUrls(string documentJson) =>
        ContentDocumentJson.Deserialize(documentJson).Blocks
            .SelectMany(block => block switch
            {
                ImageBlock image => [image.Url],
                GalleryBlock gallery => gallery.Images.Select(image => image.Url),
                _ => [],
            })
            .DistinctBy(url => url.AbsoluteUri, StringComparer.Ordinal)
            .ToArray();

    private bool DeleteLegacyDirectory(string contentIdentity)
    {
        var identityHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(contentIdentity)))[..16];
        var path = Path.GetFullPath(Path.Combine(catalog.RootPath, identityHash));
        if (!path.StartsWith(catalog.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(path))
        {
            return false;
        }

        Directory.Delete(path, recursive: true);
        return true;
    }

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Information,
        Message = "Media migration item {ItemIndex}: {ContentIdentity} ({Status}), {MediaCount} images, apply: {Apply}.")]
    private static partial void LogItemStarted(
        ILogger logger,
        int itemIndex,
        string contentIdentity,
        ContentStatus status,
        int mediaCount,
        bool apply);

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Warning,
        Message = "Media migration failed for {ContentIdentity} from {MediaUrl}.")]
    private static partial void LogMediaFailed(
        ILogger logger,
        string contentIdentity,
        Uri mediaUrl,
        Exception exception);

    [LoggerMessage(
        EventId = 3012,
        Level = LogLevel.Information,
        Message = "Media migration complete: {ContentItems} content items ({ArchivedContentItems} archived), {MediaUrls} URLs, {Migrated} migrated, {AlreadyCurrent} current, {Failed} failed, {DeletedLegacyDirectories} legacy directories deleted, apply: {Apply}.")]
    private static partial void LogCompleted(
        ILogger logger,
        int contentItems,
        int archivedContentItems,
        int mediaUrls,
        int migrated,
        int alreadyCurrent,
        int failed,
        int deletedLegacyDirectories,
        bool apply);

    [LoggerMessage(
        EventId = 3013,
        Level = LogLevel.Information,
        Message = "Media migration stored {ContentIdentity} from {MediaUrl}: {State}, {OriginalLengthBytes} -> {StoredLengthBytes} bytes.")]
    private static partial void LogMediaStored(
        ILogger logger,
        string contentIdentity,
        Uri mediaUrl,
        MediaArchiveState state,
        long originalLengthBytes,
        long storedLengthBytes);
}
