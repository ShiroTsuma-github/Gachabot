using GachaBot.Domain.Content;
using GachaBot.Domain.Games;
using GachaBot.Application.Media;
using GachaBot.Application.Publishing;

namespace GachaBot.Infrastructure.Database;

public sealed class ContentRecord
{
    public Guid Id { get; set; }

    public required string Identity { get; set; }

    public required string SourceKey { get; set; }

    public string? ExternalId { get; set; }

    public GameKey Game { get; set; }

    public ContentKind Kind { get; set; }

    public required string Title { get; set; }

    public string? SourceUrl { get; set; }

    public required string DocumentJson { get; set; }

    public required string DocumentHash { get; set; }

    public ContentStatus Status { get; set; }

    public bool AwaitingReview { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? SourcePublishedAtUtc { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public DateTimeOffset? ScheduledAtUtc { get; set; }

    public DateTimeOffset? PublishedAtUtc { get; set; }

    public DateTimeOffset? ArchivedAtUtc { get; set; }

    public ArchiveReason? ArchiveReason { get; set; }

    public List<ContentRevisionRecord> Revisions { get; set; } = [];

    public List<PublicationRecord> Publications { get; set; } = [];

    public List<MediaAssetRecord> MediaAssets { get; set; } = [];
}

public sealed class MediaAssetRecord
{
    public Guid Id { get; set; }

    public Guid ContentId { get; set; }

    public ContentRecord Content { get; set; } = null!;

    public required string SourceUrl { get; set; }

    public required string RelativePath { get; set; }

    public string? ObjectKey { get; set; }

    public required string ContentType { get; set; }

    public long OriginalLength { get; set; }

    public long StoredLength { get; set; }

    public required string Sha256 { get; set; }

    public MediaArchiveState State { get; set; }

    public string? ProcessingNote { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class ContentRevisionRecord
{
    public Guid Id { get; set; }

    public Guid ContentId { get; set; }

    public ContentRecord Content { get; set; } = null!;

    public required string PreviousTitle { get; set; }

    public required string PreviousDocumentJson { get; set; }

    public required string PreviousDocumentHash { get; set; }

    public DateTimeOffset ChangedAtUtc { get; set; }
}

public enum PublicationState
{
    Pending = 1,
    Processing = 2,
    Published = 3,
    Failed = 4,
    Cancelled = 5,
}

public sealed class PublicationRecord
{
    public Guid Id { get; set; }

    public Guid ContentId { get; set; }

    public ContentRecord Content { get; set; } = null!;

    public long? DestinationGuildId { get; set; }

    public long? DestinationChannelId { get; set; }

    public DateTimeOffset DueAtUtc { get; set; }

    public PublicationState State { get; set; }

    public PublicationPurpose Purpose { get; set; } = PublicationPurpose.Standard;

    public int AttemptCount { get; set; }

    public string? ProviderMessageId { get; set; }

    public string? LastError { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class SourceStateRecord
{
    public required string SourceKey { get; set; }

    public bool HasCompletedBaseline { get; set; }

    public DateTimeOffset? LastSuccessfulRunUtc { get; set; }

    public DateTimeOffset? LastAttemptUtc { get; set; }

    public string? LastFailureMessage { get; set; }

    public string? ETag { get; set; }

    public DateTimeOffset? LastModifiedUtc { get; set; }
}
