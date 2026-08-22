using GachaBot.Application.Media;
using GachaBot.Application.Ingestion;
using GachaBot.Application.Publishing;
using GachaBot.Domain.Content;
using GachaBot.Domain.Games;
using GachaBot.Infrastructure.Discord;
using GachaBot.Infrastructure.Media;
using Microsoft.Extensions.Options;

namespace GachaBot.Infrastructure.IntegrationTests;

public sealed class DiscordMediaMessagePlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"gachabot-discord-media-{Guid.NewGuid():N}");

    [Fact]
    public async Task PrepareAsync_WithTwoArchivedImages_SplitsIntoOneAttachmentPerMessage()
    {
        var catalog = CreateCatalog();
        var firstUrl = new Uri("https://cdn.example.com/one.png");
        var secondUrl = new Uri("https://cdn.example.com/two.png");
        await AddArchivedAsync(catalog, firstUrl, "first");
        await AddArchivedAsync(catalog, secondUrl, "second");
        var document = ContentDocument.Create(
        [
            new ImageBlock(firstUrl, "First", 1),
            new ImageBlock(secondUrl, "Second", 2),
        ]);
        var payload = Payload(document);
        var composition = DiscordMessageComposer.Compose(payload.Title, document, payload.SourceUrl);

        var result = await CreatePlanner(catalog).PrepareAsync(
            payload,
            composition,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.All(result, message => Assert.Single(message.Attachments));
        Assert.All(result, message => Assert.NotNull(Assert.Single(message.Embeds).Image?.AttachmentFileName));
    }

    [Fact]
    public async Task PrepareAsync_WhenArchiveIsMissing_KeepsRemoteImageUrl()
    {
        var catalog = CreateCatalog();
        var imageUrl = new Uri("https://cdn.example.com/missing.png");
        var document = ContentDocument.Create([new ImageBlock(imageUrl, "Missing", 1)]);
        var payload = Payload(document);

        var result = await CreatePlanner(catalog).PrepareAsync(
            payload,
            DiscordMessageComposer.Compose(payload.Title, document, payload.SourceUrl),
            TestContext.Current.CancellationToken);

        var message = Assert.Single(result);
        Assert.Empty(message.Attachments);
        Assert.Equal(imageUrl, Assert.Single(message.Embeds).Image?.Url);
        Assert.Null(Assert.Single(message.Embeds).Image?.AttachmentFileName);
    }

    [Fact]
    public async Task PrepareAsync_ForOfficialSource_UsesRemoteUrlEvenWhenArchiveExists()
    {
        var catalog = CreateCatalog();
        var imageUrl = new Uri("https://cdn.example.com/official.png");
        await AddArchivedAsync(catalog, imageUrl, "official");
        var document = ContentDocument.Create([new ImageBlock(imageUrl, "Official", 1)]);
        var payload = Payload(document);

        var result = await CreatePlanner(catalog, official: true).PrepareAsync(
            payload,
            DiscordMessageComposer.Compose(payload.Title, document, payload.SourceUrl),
            TestContext.Current.CancellationToken);

        var message = Assert.Single(result);
        Assert.Empty(message.Attachments);
        var image = Assert.Single(message.Embeds).Image;
        Assert.Equal(imageUrl, image?.Url);
        Assert.Null(image?.AttachmentFileName);
    }

    [Fact]
    public async Task PrepareAsync_ForOfficialEvent_UsesArchivedAttachment()
    {
        var catalog = CreateCatalog();
        var imageUrl = new Uri("https://cdn.example.com/official-event.png");
        await AddArchivedAsync(catalog, imageUrl, "official-event");
        var document = ContentDocument.Create([new ImageBlock(imageUrl, "Official event", 1)]);
        var payload = Payload(document, ContentKind.Event);

        var result = await CreatePlanner(catalog, official: true).PrepareAsync(
            payload,
            DiscordMessageComposer.Compose(payload.Title, document, payload.SourceUrl),
            TestContext.Current.CancellationToken);

        var message = Assert.Single(result);
        Assert.Single(message.Attachments);
        Assert.NotNull(Assert.Single(message.Embeds).Image?.AttachmentFileName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private MediaArchiveCatalog CreateCatalog() => new(Options.Create(new MediaArchiveOptions
    {
        RootPath = _root,
    }));

    private async Task AddArchivedAsync(MediaArchiveCatalog catalog, Uri sourceUrl, string shaPrefix)
    {
        var directory = catalog.GetItemDirectory("official", "42");
        Directory.CreateDirectory(directory);
        var sha = shaPrefix.PadRight(64, '0');
        var relativePath = $"official/42/{sha}.webp";
        await File.WriteAllBytesAsync(
            Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)),
            [1, 2, 3],
            TestContext.Current.CancellationToken);
        await catalog.SaveAsync(
            "official",
            "42",
            sourceUrl,
            new ArchivedMedia(
                relativePath,
                "image/webp",
                3,
                sha,
                3,
                false,
                MediaArchiveState.StoredOriginal,
                null),
            TestContext.Current.CancellationToken);
    }

    private static DiscordMediaMessagePlanner CreatePlanner(
        MediaArchiveCatalog catalog,
        bool official = false) => new(
            catalog,
            new SourceMediaPublicationPolicy(new Dictionary<string, SourceTrust>(StringComparer.Ordinal)
            {
                ["official"] = official ? SourceTrust.Official : SourceTrust.ReviewRequired,
            }));

    private static PublicationPayload Payload(
        ContentDocument document,
        ContentKind kind = ContentKind.News) => new(
        Guid.NewGuid(),
        "official",
        "42",
        GameKey.WutheringWaves,
        kind,
        new GuildDestination(1, 1, 0, true, GuildDestinationGames.All, DateTimeOffset.MinValue),
        "Update",
        new Uri("https://example.com/source"),
        document);
}
