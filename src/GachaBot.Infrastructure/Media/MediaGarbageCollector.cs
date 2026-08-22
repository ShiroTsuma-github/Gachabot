using GachaBot.Application.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GachaBot.Infrastructure.Media;

public sealed partial class MediaGarbageCollector(
    IMediaObjectStore objectStore,
    MediaAssetRegistry registry,
    IOptions<S3MediaOptions> options,
    TimeProvider timeProvider,
    ILogger<MediaGarbageCollector> logger) : IMediaGarbageCollector
{
    public async Task<MediaGarbageCollectionResult> CollectAsync(
        bool apply,
        CancellationToken cancellationToken)
    {
        var references = await registry.GetReferencedObjectKeysAsync(cancellationToken).ConfigureAwait(false);
        var cutoff = timeProvider.GetUtcNow().AddHours(-options.Value.GarbageCollectionGraceHours);
        var scanned = 0;
        var referenced = 0;
        var protectedByGrace = 0;
        var candidates = 0;
        var deleted = 0;
        await foreach (var item in objectStore.ListAsync(cancellationToken).ConfigureAwait(false))
        {
            scanned++;
            if (references.Contains(item.ObjectKey))
            {
                referenced++;
                continue;
            }

            if (item.LastModifiedUtc > cutoff)
            {
                protectedByGrace++;
                continue;
            }

            candidates++;
            if (apply)
            {
                await objectStore.DeleteAsync(item.ObjectKey, cancellationToken).ConfigureAwait(false);
                deleted++;
            }
        }

        var result = new MediaGarbageCollectionResult(
            scanned, referenced, protectedByGrace, candidates, deleted, apply);
        LogCompleted(logger, scanned, referenced, protectedByGrace, candidates, deleted, apply);
        return result;
    }

    [LoggerMessage(
        EventId = 3020,
        Level = LogLevel.Information,
        Message = "Media garbage collection complete: {Scanned} scanned, {Referenced} referenced, {ProtectedByGracePeriod} protected, {Candidates} candidates, {Deleted} deleted, apply: {Apply}.")]
    private static partial void LogCompleted(
        ILogger logger,
        int scanned,
        int referenced,
        int protectedByGracePeriod,
        int candidates,
        int deleted,
        bool apply);
}
