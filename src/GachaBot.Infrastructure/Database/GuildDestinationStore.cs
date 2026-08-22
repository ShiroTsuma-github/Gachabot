using GachaBot.Domain.Content;
using GachaBot.Application.Publishing;
using GachaBot.Domain.Games;
using Microsoft.EntityFrameworkCore;

namespace GachaBot.Infrastructure.Database;

file static class GuildDestinationGameSelection
{
    internal const int AllGamesMask = (1 << (int)GameKey.WutheringWaves) |
        (1 << (int)GameKey.NevernessToEverness);
}

public sealed class GuildDestinationRecord
{
    public long GuildId { get; set; }

    public required string GuildName { get; set; }

    public long ChannelId { get; set; }

    public required string ChannelName { get; set; }

    public long ConfiguredByUserId { get; set; }

    public bool IsEnabled { get; set; }

    public int EnabledGameMask { get; set; }

    public bool TopicSubscriptionsInitialized { get; set; }

    public double EventStartOffsetHours { get; set; }

    public double EventEndOffsetHours { get; set; } = 48;

    public DateTimeOffset? RemovedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class GuildTopicSubscriptionRecord
{
    public long GuildId { get; set; }

    public GameKey Game { get; set; }

    public ContentKind Kind { get; set; }
}

public sealed class GuildConfigurationDbContext(DbContextOptions<GuildConfigurationDbContext> options)
    : DbContext(options)
{
    public DbSet<GuildDestinationRecord> GuildDestinations => Set<GuildDestinationRecord>();

    public DbSet<GuildTopicSubscriptionRecord> GuildTopicSubscriptions => Set<GuildTopicSubscriptionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GuildDestinationRecord>(entity =>
        {
            entity.ToTable("GuildDestinations");
            entity.HasKey(destination => destination.GuildId);
            entity.HasIndex(destination => new { destination.IsEnabled, destination.ChannelId });
            entity.Property(destination => destination.GuildName).HasMaxLength(128);
            entity.Property(destination => destination.ChannelName).HasMaxLength(128);
            entity.Property(destination => destination.EnabledGameMask)
                .HasDefaultValue(GuildDestinationGameSelection.AllGamesMask);
            entity.Property(destination => destination.TopicSubscriptionsInitialized).HasDefaultValue(false);
            entity.Property(destination => destination.EventStartOffsetHours).HasDefaultValue(0d);
            entity.Property(destination => destination.EventEndOffsetHours).HasDefaultValue(48d);
            entity.Property(destination => destination.RemovedAtUtc);
        });
        modelBuilder.Entity<GuildTopicSubscriptionRecord>(entity =>
        {
            entity.ToTable("GuildTopicSubscriptions");
            entity.HasKey(subscription => new { subscription.GuildId, subscription.Game, subscription.Kind });
            entity.HasIndex(subscription => subscription.GuildId);
        });
    }
}

public interface IGuildConfigurationDatabaseFactory
{
    GuildConfigurationDbContext CreateDbContext();
}

public sealed class GuildConfigurationDatabaseFactory(string connectionString)
    : IGuildConfigurationDatabaseFactory
{
    private readonly string _connectionString = string.IsNullOrWhiteSpace(connectionString)
        ? throw new ArgumentException("PostgreSQL connection string is required.", nameof(connectionString))
        : connectionString;

    public GuildConfigurationDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<GuildConfigurationDbContext>()
            .UseNpgsql(_connectionString)
            .Options);
}

public sealed class GuildDestinationStore(
    IGuildConfigurationDatabaseFactory databaseFactory,
    TimeProvider timeProvider) : IGuildDestinationStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var context = databaseFactory.CreateDbContext();
        await context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);
        if (!context.Database.IsNpgsql())
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"GuildDestinations\" ADD COLUMN IF NOT EXISTS \"EnabledGameMask\" integer NOT NULL DEFAULT 6;",
            cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"GuildDestinations\" ADD COLUMN IF NOT EXISTS \"TopicSubscriptionsInitialized\" boolean NOT NULL DEFAULT FALSE;",
            cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"GuildDestinations\" ADD COLUMN IF NOT EXISTS \"GuildName\" character varying(128) NOT NULL DEFAULT '';",
            cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"GuildDestinations\" ADD COLUMN IF NOT EXISTS \"ChannelName\" character varying(128) NOT NULL DEFAULT '';",
            cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"GuildDestinations\" ADD COLUMN IF NOT EXISTS \"EventStartOffsetHours\" double precision NOT NULL DEFAULT 0;",
            cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"GuildDestinations\" ADD COLUMN IF NOT EXISTS \"EventEndOffsetHours\" double precision NOT NULL DEFAULT 48;",
            cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            "ALTER TABLE \"GuildDestinations\" ADD COLUMN IF NOT EXISTS \"RemovedAtUtc\" timestamp with time zone NULL;",
            cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "GuildTopicSubscriptions" (
                "GuildId" bigint NOT NULL,
                "Game" integer NOT NULL,
                "Kind" integer NOT NULL,
                CONSTRAINT "PK_GuildTopicSubscriptions" PRIMARY KEY ("GuildId", "Game", "Kind")
            );
            """,
            cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "GuildTopicSubscriptions" ("GuildId", "Game", "Kind")
            SELECT destinations."GuildId", games."Game", kinds."Kind"
            FROM "GuildDestinations" AS destinations
            CROSS JOIN (VALUES (1), (2)) AS games("Game")
            CROSS JOIN (VALUES (1), (2), (3), (4), (5), (6), (7)) AS kinds("Kind")
            WHERE NOT destinations."TopicSubscriptionsInitialized"
              AND (destinations."EnabledGameMask" & (1 << games."Game")) <> 0
            ON CONFLICT DO NOTHING;
            """,
            cancellationToken).ConfigureAwait(false);
        await context.Database.ExecuteSqlRawAsync(
            "UPDATE \"GuildDestinations\" SET \"TopicSubscriptionsInitialized\" = TRUE WHERE NOT \"TopicSubscriptionsInitialized\";",
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GuildDestination>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = databaseFactory.CreateDbContext();
        var destinations = await context.GuildDestinations.AsNoTracking()
            .OrderBy(destination => destination.GuildId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var subscriptions = await context.GuildTopicSubscriptions.AsNoTracking()
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ToContracts(destinations, subscriptions);
    }

    public async Task<IReadOnlyList<GuildDestination>> ListActiveAsync(CancellationToken cancellationToken)
    {
        await using var context = databaseFactory.CreateDbContext();
        var destinations = await context.GuildDestinations.AsNoTracking()
            .Where(destination => destination.IsEnabled)
            .OrderBy(destination => destination.GuildId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var guildIds = destinations.Select(destination => destination.GuildId).ToArray();
        var subscriptions = await context.GuildTopicSubscriptions.AsNoTracking()
            .Where(subscription => guildIds.Contains(subscription.GuildId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ToContracts(destinations, subscriptions);
    }

    public async Task ConfigureAsync(
        ulong guildId,
        string guildName,
        ulong channelId,
        string channelName,
        ulong configuredByUserId,
        IReadOnlySet<GameKey> games,
        CancellationToken cancellationToken)
    {
        ValidateSnowflake(guildId, nameof(guildId));
        ValidateSnowflake(channelId, nameof(channelId));
        ValidateSnowflake(configuredByUserId, nameof(configuredByUserId));
        ArgumentException.ThrowIfNullOrWhiteSpace(guildName);
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        var gameMask = ToMask(games);
        await using var context = databaseFactory.CreateDbContext();
        var persistedGuildId = checked((long)guildId);
        var destination = await context.GuildDestinations.SingleOrDefaultAsync(
            item => item.GuildId == persistedGuildId,
            cancellationToken).ConfigureAwait(false);
        var now = timeProvider.GetUtcNow();
        if (destination is null)
        {
            destination = new GuildDestinationRecord
            {
                GuildId = persistedGuildId,
                GuildName = guildName.Trim(),
                ChannelId = checked((long)channelId),
                ChannelName = channelName.Trim(),
                ConfiguredByUserId = checked((long)configuredByUserId),
                IsEnabled = true,
                EnabledGameMask = gameMask,
                TopicSubscriptionsInitialized = true,
                UpdatedAtUtc = now,
            };
            context.GuildDestinations.Add(destination);
            AddTopicSubscriptions(context, destination.GuildId, games);
        }
        else
        {
            var previouslyEnabledGames = FromMask(destination.EnabledGameMask);
            var needsTopicInitialization = !destination.TopicSubscriptionsInitialized;
            destination.GuildName = guildName.Trim();
            destination.ChannelId = checked((long)channelId);
            destination.ChannelName = channelName.Trim();
            destination.ConfiguredByUserId = checked((long)configuredByUserId);
            destination.IsEnabled = true;
            destination.EnabledGameMask = gameMask;
            destination.TopicSubscriptionsInitialized = true;
            destination.RemovedAtUtc = null;
            destination.UpdatedAtUtc = now;
            var existingSubscriptions = await context.GuildTopicSubscriptions
                .Where(subscription => subscription.GuildId == persistedGuildId)
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            context.GuildTopicSubscriptions.RemoveRange(existingSubscriptions.Where(subscription => !games.Contains(subscription.Game)));
            AddTopicSubscriptions(
                context,
                persistedGuildId,
                games.Where(game => needsTopicInitialization || !previouslyEnabledGames.Contains(game)));
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetEnabledAsync(ulong guildId, bool enabled, CancellationToken cancellationToken)
    {
        ValidateSnowflake(guildId, nameof(guildId));
        await using var context = databaseFactory.CreateDbContext();
        var destination = await context.GuildDestinations.SingleOrDefaultAsync(
            item => item.GuildId == checked((long)guildId), cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Guild '{guildId}' is not configured.");
        destination.IsEnabled = enabled;
        destination.UpdatedAtUtc = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetEventNotificationOffsetsAsync(
        ulong guildId,
        double startOffsetHours,
        double endOffsetHours,
        CancellationToken cancellationToken)
    {
        ValidateSnowflake(guildId, nameof(guildId));
        ValidateOffset(startOffsetHours, nameof(startOffsetHours));
        ValidateOffset(endOffsetHours, nameof(endOffsetHours));
        await using var context = databaseFactory.CreateDbContext();
        var destination = await context.GuildDestinations.SingleOrDefaultAsync(
            item => item.GuildId == checked((long)guildId), cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Guild '{guildId}' is not configured.");
        destination.EventStartOffsetHours = startOffsetHours;
        destination.EventEndOffsetHours = endOffsetHours;
        destination.UpdatedAtUtc = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetTopicSubscriptionsAsync(
        ulong guildId,
        GameKey game,
        IReadOnlySet<ContentKind> kinds,
        CancellationToken cancellationToken)
    {
        ValidateSnowflake(guildId, nameof(guildId));
        ArgumentNullException.ThrowIfNull(kinds);
        if (!Enum.IsDefined(game) || kinds.Any(kind => !Enum.IsDefined(kind)))
        {
            throw new ArgumentOutOfRangeException(nameof(game), "Unknown game or content subject.");
        }

        await using var context = databaseFactory.CreateDbContext();
        var destination = await context.GuildDestinations.SingleOrDefaultAsync(
            item => item.GuildId == checked((long)guildId), cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Guild '{guildId}' is not configured.");
        if (!FromMask(destination.EnabledGameMask).Contains(game))
        {
            throw new InvalidOperationException("Enable this game before choosing its subjects.");
        }

        var existing = await context.GuildTopicSubscriptions
            .Where(subscription => subscription.GuildId == destination.GuildId && subscription.Game == game)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        context.GuildTopicSubscriptions.RemoveRange(existing);
        AddTopicSubscriptions(context, destination.GuildId, [game], kinds);
        destination.UpdatedAtUtc = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkRemovedAsync(ulong guildId, CancellationToken cancellationToken)
    {
        ValidateSnowflake(guildId, nameof(guildId));
        await using var context = databaseFactory.CreateDbContext();
        var destination = await context.GuildDestinations.SingleOrDefaultAsync(
            item => item.GuildId == checked((long)guildId), cancellationToken).ConfigureAwait(false);
        if (destination is null)
        {
            return;
        }

        destination.IsEnabled = false;
        destination.RemovedAtUtc ??= timeProvider.GetUtcNow();
        destination.UpdatedAtUtc = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreRemovedAsync(ulong guildId, CancellationToken cancellationToken)
    {
        ValidateSnowflake(guildId, nameof(guildId));
        await using var context = databaseFactory.CreateDbContext();
        var destination = await context.GuildDestinations.SingleOrDefaultAsync(
            item => item.GuildId == checked((long)guildId), cancellationToken).ConfigureAwait(false);
        if (destination is null || destination.RemovedAtUtc is null)
        {
            return;
        }

        destination.RemovedAtUtc = null;
        destination.UpdatedAtUtc = timeProvider.GetUtcNow();
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<GuildDestination>> ListRemovedBeforeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var context = databaseFactory.CreateDbContext();
        var destinations = await context.GuildDestinations.AsNoTracking()
            .Where(destination => destination.RemovedAtUtc != null && destination.RemovedAtUtc <= cutoffUtc)
            .OrderBy(destination => destination.RemovedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return ToContracts(destinations, []);
    }

    public async Task<bool> DeleteRemovedAsync(
        ulong guildId,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        ValidateSnowflake(guildId, nameof(guildId));
        await using var context = databaseFactory.CreateDbContext();
        var destination = await context.GuildDestinations.SingleOrDefaultAsync(
            item => item.GuildId == checked((long)guildId) &&
                    item.RemovedAtUtc != null &&
                    item.RemovedAtUtc <= cutoffUtc,
            cancellationToken).ConfigureAwait(false);
        if (destination is null)
        {
            return false;
        }

        context.GuildDestinations.Remove(destination);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> IsAdministratorAsync(ulong userId, CancellationToken cancellationToken)
    {
        ValidateSnowflake(userId, nameof(userId));
        await using var context = databaseFactory.CreateDbContext();
        return await context.GuildDestinations.AnyAsync(
            destination => destination.ConfiguredByUserId == checked((long)userId), cancellationToken)
            .ConfigureAwait(false);
    }

    private static GuildDestination[] ToContracts(
        IReadOnlyList<GuildDestinationRecord> destinations,
        IReadOnlyList<GuildTopicSubscriptionRecord> subscriptions)
    {
        var byGuild = subscriptions.ToLookup(subscription => subscription.GuildId);
        return destinations.Select(destination => ToContract(destination, byGuild[destination.GuildId])).ToArray();
    }

    private static GuildDestination ToContract(
        GuildDestinationRecord destination,
        IEnumerable<GuildTopicSubscriptionRecord> subscriptions) => new(
        checked((ulong)destination.GuildId),
        checked((ulong)destination.ChannelId),
        checked((ulong)destination.ConfiguredByUserId),
        destination.IsEnabled,
        FromMask(destination.EnabledGameMask),
        destination.UpdatedAtUtc,
        destination.GuildName,
        destination.ChannelName,
        destination.EventStartOffsetHours,
        destination.EventEndOffsetHours,
        destination.RemovedAtUtc,
        subscriptions.Select(subscription => new GuildTopicSubscription(subscription.Game, subscription.Kind)).ToHashSet());

    private static void AddTopicSubscriptions(
        GuildConfigurationDbContext context,
        long guildId,
        IEnumerable<GameKey> games,
        IEnumerable<ContentKind>? kinds = null)
    {
        var selectedKinds = kinds ?? Enum.GetValues<ContentKind>();
        context.GuildTopicSubscriptions.AddRange(games.SelectMany(game => selectedKinds
            .Select(kind => new GuildTopicSubscriptionRecord { GuildId = guildId, Game = game, Kind = kind })));
    }

    private static int ToMask(IReadOnlySet<GameKey> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        if (games.Count == 0)
        {
            throw new ArgumentException("At least one game must be selected.", nameof(games));
        }

        var mask = 0;
        foreach (var game in games)
        {
            if (!Enum.IsDefined(game))
            {
                throw new ArgumentOutOfRangeException(nameof(games), game, "Unknown game.");
            }

            mask |= 1 << (int)game;
        }

        return mask;
    }

    private static IReadOnlySet<GameKey> FromMask(int mask)
    {
        var games = new HashSet<GameKey>();
        foreach (var game in Enum.GetValues<GameKey>())
        {
            if ((mask & (1 << (int)game)) != 0)
            {
                games.Add(game);
            }
        }

        return games.Count == 0 ? GuildDestinationGames.All : games;
    }

    private static void ValidateSnowflake(ulong value, string parameterName)
    {
        if (value == 0 || value > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Discord snowflake is invalid.");
        }
    }

    private static void ValidateOffset(double value, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value is < 0 or > 72)
        {
            throw new ArgumentOutOfRangeException(parameterName, "The event notification offset must be between 0 and 72 hours.");
        }
    }
}

public sealed class LegacyGuildDestinationStore : IGuildDestinationStore
{
    private static readonly GuildDestination LegacyDestination = new(
        1,
        1,
        0,
        true,
        GuildDestinationGames.All,
        DateTimeOffset.MinValue);

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<IReadOnlyList<GuildDestination>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GuildDestination>>([LegacyDestination]);

    public Task<IReadOnlyList<GuildDestination>> ListActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GuildDestination>>([LegacyDestination]);

    public Task ConfigureAsync(
        ulong guildId,
        string guildName,
        ulong channelId,
        string channelName,
        ulong configuredByUserId,
        IReadOnlySet<GameKey> games,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The legacy destination store is for compatibility tests only.");

    public Task SetEnabledAsync(ulong guildId, bool enabled, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The legacy destination store is for compatibility tests only.");

    public Task SetEventNotificationOffsetsAsync(
        ulong guildId,
        double startOffsetHours,
        double endOffsetHours,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The legacy destination store is for compatibility tests only.");

    public Task SetTopicSubscriptionsAsync(
        ulong guildId,
        GameKey game,
        IReadOnlySet<ContentKind> kinds,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("The legacy destination store is for compatibility tests only.");

    public Task MarkRemovedAsync(ulong guildId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The legacy destination store is for compatibility tests only.");

    public Task RestoreRemovedAsync(ulong guildId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The legacy destination store is for compatibility tests only.");

    public Task<IReadOnlyList<GuildDestination>> ListRemovedBeforeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GuildDestination>>([]);

    public Task<bool> DeleteRemovedAsync(
        ulong guildId,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> IsAdministratorAsync(ulong userId, CancellationToken cancellationToken) => Task.FromResult(false);
}
