using GachaBot.Application.Media;
using GachaBot.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace GachaBot.Infrastructure.Media;

public sealed record StoredMediaAsset(string ObjectKey, string ContentType, string Sha256, long StoredLength);

public sealed class MediaAssetRegistry(
    ISourceDatabaseFactory databaseFactory,
    TimeProvider timeProvider)
{
    public async Task<bool> IsRecordedAsync(
        string sourceKey,
        string externalId,
        Uri sourceUrl,
        CancellationToken cancellationToken)
    {
        await using var context = databaseFactory.CreateDbContext(sourceKey);
        return await context.MediaAssets.AnyAsync(
            asset =>
                asset.Content.SourceKey == sourceKey &&
                asset.Content.ExternalId == externalId &&
                asset.SourceUrl == sourceUrl.AbsoluteUri &&
                asset.ObjectKey != null,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<StoredMediaAsset?> TryGetAsync(
        string sourceKey,
        string externalId,
        Uri sourceUrl,
        CancellationToken cancellationToken)
    {
        await using var context = databaseFactory.CreateDbContext(sourceKey);
        return await context.MediaAssets.AsNoTracking()
            .Where(asset => asset.Content.SourceKey == sourceKey &&
                asset.Content.ExternalId == externalId &&
                asset.SourceUrl == sourceUrl.AbsoluteUri &&
                asset.ObjectKey != null)
            .Select(asset => new StoredMediaAsset(
                asset.ObjectKey!, asset.ContentType, asset.Sha256, asset.StoredLength))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAbsentAsync(
        MediaArchiveRequest request,
        IReadOnlyCollection<Uri> currentUrls,
        CancellationToken cancellationToken)
    {
        await using var context = databaseFactory.CreateDbContext(request.SourceKey);
        var contentId = await context.ContentItems
            .Where(content => content.SourceKey == request.SourceKey && content.ExternalId == request.ExternalId)
            .Select(content => (Guid?)content.Id)
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (contentId is null)
        {
            return;
        }

        var urls = currentUrls.Select(url => url.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
        var obsolete = await context.MediaAssets
            .Where(asset => asset.ContentId == contentId && !urls.Contains(asset.SourceUrl))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        if (obsolete.Count > 0)
        {
            context.MediaAssets.RemoveRange(obsolete);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlySet<string>> GetReferencedObjectKeysAsync(CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sourceKey in databaseFactory.DatabaseKeys)
        {
            await using var context = databaseFactory.CreateDbContext(sourceKey);
            foreach (var key in await context.MediaAssets.AsNoTracking()
                         .Where(asset => asset.ObjectKey != null)
                         .Select(asset => asset.ObjectKey!)
                         .ToListAsync(cancellationToken).ConfigureAwait(false))
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    public async Task RecordAsync(
        MediaArchiveRequest request,
        ArchivedMedia archived,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(archived);
        await using var context = databaseFactory.CreateDbContext(request.SourceKey);
        var contentId = await context.ContentItems
            .Where(content =>
                content.SourceKey == request.SourceKey &&
                content.ExternalId == request.ExternalId)
            .Select(content => (Guid?)content.Id)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Cannot register media because content '{request.ContentIdentity}' does not exist.");
        var sourceUrl = request.SourceUrl.AbsoluteUri;
        var record = await context.MediaAssets.SingleOrDefaultAsync(
            asset => asset.ContentId == contentId && asset.SourceUrl == sourceUrl,
            cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            record = new MediaAssetRecord
            {
                Id = Guid.NewGuid(),
                ContentId = contentId,
                SourceUrl = sourceUrl,
                RelativePath = archived.RelativePath,
                ObjectKey = archived.ObjectKey ?? archived.RelativePath,
                ContentType = archived.ContentType,
                Sha256 = archived.Sha256,
            };
            context.MediaAssets.Add(record);
        }

        record.RelativePath = archived.RelativePath;
        record.ObjectKey = archived.ObjectKey ?? archived.RelativePath;
        record.ContentType = archived.ContentType;
        record.OriginalLength = archived.OriginalLength;
        record.StoredLength = archived.Length;
        record.Sha256 = archived.Sha256;
        record.State = archived.State;
        record.ProcessingNote = archived.ProcessingNote;
        record.UpdatedAtUtc = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
