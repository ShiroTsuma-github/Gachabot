using GachaBot.Application.Publishing;
using GachaBot.Domain.Content;
using GachaBot.Domain.Games;
using GachaBot.Infrastructure.Database;
using GachaBot.Infrastructure.Publishing;
using Microsoft.EntityFrameworkCore;

namespace GachaBot.Infrastructure.IntegrationTests;

public sealed class GuildRemovalServiceTests
{
    [Fact]
    public async Task CancelPendingAsync_CancelsOnlyUndeliveredPostsForTheRemovedGuild()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var factory = new InMemorySourceDatabaseFactory(databaseName);
        await using (var context = factory.CreateDbContext("test"))
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var content = Content();
            context.ContentItems.Add(content);
            context.Publications.AddRange(
                Publication(content, 100, PublicationState.Pending),
                Publication(content, 100, PublicationState.Processing),
                Publication(content, 100, PublicationState.Published),
                Publication(content, 200, PublicationState.Pending));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = new GuildRemovalService(factory, TimeProvider.System);
        await service.CancelPendingAsync(100, TestContext.Current.CancellationToken);

        await using var assertionContext = factory.CreateDbContext("test");
        var publications = await assertionContext.Publications
            .OrderBy(item => item.DestinationGuildId)
            .ThenBy(item => item.State)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, publications.Count(item => item.DestinationGuildId == 100 && item.State == PublicationState.Cancelled));
        Assert.Single(publications, item => item.DestinationGuildId == 100 && item.State == PublicationState.Published);
        Assert.Single(publications, item => item.DestinationGuildId == 200 && item.State == PublicationState.Pending);
    }

    [Fact]
    public async Task DeleteHistoryAsync_RemovesOnlyTheRemovedGuildsPublicationHistory()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var factory = new InMemorySourceDatabaseFactory(databaseName);
        await using (var context = factory.CreateDbContext("test"))
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var content = Content();
            context.ContentItems.Add(content);
            context.Publications.AddRange(
                Publication(content, 100, PublicationState.Published),
                Publication(content, 200, PublicationState.Published));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = new GuildRemovalService(factory, TimeProvider.System);
        await service.DeleteHistoryAsync(100, TestContext.Current.CancellationToken);

        await using var assertionContext = factory.CreateDbContext("test");
        Assert.DoesNotContain(await assertionContext.Publications.ToListAsync(TestContext.Current.CancellationToken), item => item.DestinationGuildId == 100);
        Assert.Single(await assertionContext.Publications.ToListAsync(TestContext.Current.CancellationToken), item => item.DestinationGuildId == 200);
    }

    private static ContentRecord Content() => new()
    {
        Id = Guid.NewGuid(),
        Identity = "test:content",
        SourceKey = "test",
        Game = GameKey.WutheringWaves,
        Kind = ContentKind.Event,
        Title = "Test event",
        DocumentJson = "[]",
        DocumentHash = "hash",
        Status = ContentStatus.Published,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    private static PublicationRecord Publication(ContentRecord content, long guildId, PublicationState state) => new()
    {
        Id = Guid.NewGuid(),
        Content = content,
        ContentId = content.Id,
        DestinationGuildId = guildId,
        DestinationChannelId = guildId + 1,
        DueAtUtc = DateTimeOffset.UtcNow,
        State = state,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow,
    };

    private sealed class InMemorySourceDatabaseFactory(string databaseName) : ISourceDatabaseFactory
    {
        public IReadOnlyList<string> DatabaseKeys { get; } = ["test"];

        public AppDbContext CreateDbContext(string sourceKey) => new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options);
    }
}
