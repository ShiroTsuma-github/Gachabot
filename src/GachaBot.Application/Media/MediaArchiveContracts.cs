namespace GachaBot.Application.Media;

public enum MediaArchiveState
{
    Compressed = 1,
    StoredOriginal = 2,
    Uncompressable = 3,
}

public sealed record ArchivedMedia(
    string RelativePath,
    string ContentType,
    long Length,
    string Sha256,
    long OriginalLength,
    bool WasCompressed,
    MediaArchiveState State,
    string? ProcessingNote,
    string? ObjectKey = null);

public sealed record MediaArchiveRequest(
    string SourceKey,
    string ExternalId,
    string ContentIdentity,
    Uri SourceUrl);

public interface IMediaArchive
{
    Task<ArchivedMedia> ArchiveAsync(
        MediaArchiveRequest request,
        CancellationToken cancellationToken);
}

public sealed record MediaGarbageCollectionResult(
    int Scanned,
    int Referenced,
    int ProtectedByGracePeriod,
    int Candidates,
    int Deleted,
    bool Applied);

public interface IMediaGarbageCollector
{
    Task<MediaGarbageCollectionResult> CollectAsync(bool apply, CancellationToken cancellationToken);
}
