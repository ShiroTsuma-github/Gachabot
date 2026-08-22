using GachaBot.Application.Publishing;
using GachaBot.Domain.Games;
using GachaBot.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace GachaBot.Infrastructure.IntegrationTests;

public sealed class GuildDestinationStoreTests
{
    [Fact]
    public async Task ConfigureAsync_NewGuildStartsPaused_AndExistingEnabledGuildStaysEnabled()
    {
        var factory = new InMemoryGuildConfigurationDatabaseFactory(Guid.NewGuid().ToString("N"));
        var store = new GuildDestinationStore(factory, TimeProvider.System);
        await store.InitializeAsync(TestContext.Current.CancellationToken);

        await store.ConfigureAsync(
            100,
            "Test guild",
            200,
            "updates",
            300,
            new HashSet<GameKey> { GameKey.WutheringWaves },
            TestContext.Current.CancellationToken);

        var configured = Assert.Single(await store.ListAsync(TestContext.Current.CancellationToken));
        Assert.False(configured.IsEnabled);

        await store.SetEnabledAsync(100, true, TestContext.Current.CancellationToken);
        await store.ConfigureAsync(
            100,
            "Test guild",
            201,
            "news",
            300,
            new HashSet<GameKey> { GameKey.NevernessToEverness },
            TestContext.Current.CancellationToken);

        var reconfigured = Assert.Single(await store.ListAsync(TestContext.Current.CancellationToken));
        Assert.True(reconfigured.IsEnabled);
        Assert.Equal((ulong)201, reconfigured.ChannelId);
        Assert.Equal(new HashSet<GameKey> { GameKey.NevernessToEverness }, reconfigured.Games);
    }

    private sealed class InMemoryGuildConfigurationDatabaseFactory(string databaseName)
        : IGuildConfigurationDatabaseFactory
    {
        public GuildConfigurationDbContext CreateDbContext() => new(
            new DbContextOptionsBuilder<GuildConfigurationDbContext>()
                .UseInMemoryDatabase(databaseName)
                .Options);
    }
}
