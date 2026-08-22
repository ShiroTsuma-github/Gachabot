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

    public double EventStartOffsetHours { get; set; }

    public double EventEndOffsetHours { get; set; } = 48;

    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class GuildConfigurationDbContext(DbContextOptions<GuildConfigurationDbContext> options)
    : DbContext(options)
{
    public DbSet<GuildDestinationRecord> GuildDestinations => Set<GuildDestinationRecord>();

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
            entity.Property(destination => destination.EventStartOffsetHours).HasDefaultValue(0d);
            entity.Property(destination => destination.EventEndOffsetHours).HasDefaultValue(48d);
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
    }

    public async Task<IReadOnlyList<GuildDestination>> ListAsync(CancellationToken cancellationToken)
    {
        await using var context = databaseFactory.CreateDbContext();
        var destinations = await context.GuildDestinations.AsNoTracking()
            .OrderBy(destination => destination.GuildId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return destinations.Select(ToContract).ToArray();
    }

    public async Task<IReadOnlyList<GuildDestination>> ListActiveAsync(CancellationToken cancellationToken)
    {
        await using var context = databaseFactory.CreateDbContext();
        var destinations = await context.GuildDestinations.AsNoTracking()
            .Where(destination => destination.IsEnabled)
            .OrderBy(destination => destination.GuildId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return destinations.Select(ToContract).ToArray();
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
            context.GuildDestinations.Add(new GuildDestinationRecord
            {
                GuildId = persistedGuildId,
                GuildName = guildName.Trim(),
                ChannelId = checked((long)channelId),
                ChannelName = channelName.Trim(),
                ConfiguredByUserId = checked((long)configuredByUserId),
                IsEnabled = true,
                EnabledGameMask = gameMask,
                UpdatedAtUtc = now,
            });
        }
        else
        {
            destination.GuildName = guildName.Trim();
            destination.ChannelId = checked((long)channelId);
            destination.ChannelName = channelName.Trim();
            destination.ConfiguredByUserId = checked((long)configuredByUserId);
            destination.IsEnabled = true;
            destination.EnabledGameMask = gameMask;
            destination.UpdatedAtUtc = now;
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

    public async Task<bool> IsAdministratorAsync(ulong userId, CancellationToken cancellationToken)
    {
        ValidateSnowflake(userId, nameof(userId));
        await using var context = databaseFactory.CreateDbContext();
        return await context.GuildDestinations.AnyAsync(
            destination => destination.ConfiguredByUserId == checked((long)userId), cancellationToken)
            .ConfigureAwait(false);
    }

    private static GuildDestination ToContract(GuildDestinationRecord destination) => new(
        checked((ulong)destination.GuildId),
        checked((ulong)destination.ChannelId),
        checked((ulong)destination.ConfiguredByUserId),
        destination.IsEnabled,
        FromMask(destination.EnabledGameMask),
        destination.UpdatedAtUtc,
        destination.GuildName,
        destination.ChannelName,
        destination.EventStartOffsetHours,
        destination.EventEndOffsetHours);

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

    public Task<bool> IsAdministratorAsync(ulong userId, CancellationToken cancellationToken) => Task.FromResult(false);
}
