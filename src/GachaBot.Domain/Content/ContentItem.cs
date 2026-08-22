using GachaBot.Domain.Games;

namespace GachaBot.Domain.Content;

public sealed class ContentItem
{
    private readonly List<ContentRevision> _revisions = [];

    private ContentItem(
        GameKey game,
        ContentKind kind,
        string identity,
        string title,
        Uri? sourceUrl,
        ContentDocument document,
        ContentStatus status,
        DateTimeOffset createdAtUtc)
    {
        Id = Guid.NewGuid();
        Game = game;
        Kind = kind;
        Identity = ContentBlockValidation.Required(identity, nameof(identity), 512);
        Title = ContentBlockValidation.Required(title, nameof(title), 256);
        SourceUrl = sourceUrl;
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Status = status;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public GameKey Game { get; }

    public ContentKind Kind { get; }

    public string Identity { get; }

    public string Title { get; private set; }

    public Uri? SourceUrl { get; }

    public ContentDocument Document { get; private set; }

    public ContentStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? ScheduledAtUtc { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset? ArchivedAtUtc { get; private set; }

    public ArchiveReason? ArchiveReason { get; private set; }

    public IReadOnlyList<ContentRevision> Revisions => _revisions;

    public static ContentItem CreateImported(
        GameKey game,
        ContentKind kind,
        string identity,
        string title,
        Uri sourceUrl,
        ContentDocument document,
        DateTimeOffset createdAtUtc) =>
        new(game, kind, identity, title, sourceUrl, document, ContentStatus.Active, createdAtUtc);

    public static ContentItem CreateManual(
        GameKey game,
        ContentKind kind,
        string title,
        ContentDocument document,
        DateTimeOffset createdAtUtc) =>
        new(game, kind, $"manual:{Guid.NewGuid():N}", title, null, document, ContentStatus.Draft, createdAtUtc);

    public bool ApplyDocument(string title, ContentDocument document, DateTimeOffset changedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (Document.Hash == document.Hash && string.Equals(Title, title, StringComparison.Ordinal))
        {
            return false;
        }

        _revisions.Add(new ContentRevision(
            Guid.NewGuid(),
            Id,
            Title,
            Document.Hash,
            changedAtUtc));
        Title = ContentBlockValidation.Required(title, nameof(title), 256);
        Document = document;
        UpdatedAtUtc = changedAtUtc;
        return true;
    }

    public void Schedule(DateTimeOffset publishAtUtc)
    {
        if (Status == ContentStatus.Archived)
        {
            throw new DomainValidationException("Archived content cannot be scheduled.");
        }

        ScheduledAtUtc = publishAtUtc;
        Status = ContentStatus.Scheduled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void MarkPublished(DateTimeOffset publishedAtUtc)
    {
        PublishedAtUtc = publishedAtUtc;
        Status = ContentStatus.Published;
        UpdatedAtUtc = publishedAtUtc;
    }

    public void Archive(
        DateTimeOffset archivedAtUtc,
        ArchiveReason reason = GachaBot.Domain.Content.ArchiveReason.Manual)
    {
        ArchivedAtUtc = archivedAtUtc;
        ArchiveReason = reason;
        Status = ContentStatus.Archived;
        UpdatedAtUtc = archivedAtUtc;
    }
}

public sealed record ContentRevision(
    Guid Id,
    Guid ContentItemId,
    string PreviousTitle,
    string PreviousDocumentHash,
    DateTimeOffset ChangedAtUtc);
