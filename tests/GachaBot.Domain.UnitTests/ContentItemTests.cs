using GachaBot.Domain.Content;
using GachaBot.Domain.Games;

namespace GachaBot.Domain.UnitTests;

public sealed class ContentItemTests
{
    [Fact]
    public void ApplyDocument_WhenContentChanged_AppendsRevision()
    {
        var initial = ContentDocument.Create([new TextBlock("Old text", 1)]);
        var item = ContentItem.CreateImported(
            GameKey.WutheringWaves,
            ContentKind.News,
            "official:42",
            "Old title",
            new Uri("https://example.com/news/42"),
            initial,
            DateTimeOffset.Parse("2026-08-13T08:00:00Z", CultureInfo.InvariantCulture));
        var updated = ContentDocument.Create([new TextBlock("New text", 1)]);

        var changed = item.ApplyDocument(
            "New title",
            updated,
            DateTimeOffset.Parse("2026-08-13T09:00:00Z", CultureInfo.InvariantCulture));

        Assert.True(changed);
        Assert.Equal("New title", item.Title);
        Assert.Single(item.Revisions);
        Assert.Equal(initial.Hash, item.Revisions[0].PreviousDocumentHash);
        Assert.Equal(updated.Hash, item.Document.Hash);
    }

    [Fact]
    public void ApplyDocument_WhenHashUnchanged_DoesNotAppendRevision()
    {
        var document = ContentDocument.Create([new TextBlock("Same", 1)]);
        var item = ContentItem.CreateImported(
            GameKey.NevernessToEverness,
            ContentKind.Event,
            "official:event-7",
            "Event",
            new Uri("https://example.com/events/7"),
            document,
            DateTimeOffset.Parse("2026-08-13T08:00:00Z", CultureInfo.InvariantCulture));

        var changed = item.ApplyDocument(
            "Event",
            ContentDocument.Create([new TextBlock("Same", 1)]),
            DateTimeOffset.Parse("2026-08-13T09:00:00Z", CultureInfo.InvariantCulture));

        Assert.False(changed);
        Assert.Empty(item.Revisions);
    }

    [Fact]
    public void Archive_MovesItemOutOfActiveState()
    {
        var item = ContentItem.CreateManual(
            GameKey.WutheringWaves,
            ContentKind.Event,
            "Limited event",
            ContentDocument.Create([new TextBlock("Details", 1)]),
            DateTimeOffset.Parse("2026-08-13T08:00:00Z", CultureInfo.InvariantCulture));

        item.Archive(DateTimeOffset.Parse("2026-08-14T08:00:00Z", CultureInfo.InvariantCulture));

        Assert.Equal(ContentStatus.Archived, item.Status);
        Assert.Equal(
            DateTimeOffset.Parse("2026-08-14T08:00:00Z", CultureInfo.InvariantCulture),
            item.ArchivedAtUtc);
        Assert.Equal(ArchiveReason.Manual, item.ArchiveReason);
    }
}
