using GachaBot.Domain.Content;
using GachaBot.Domain.Games;

namespace GachaBot.Application.Publishing;

public sealed record GuildDestination(
    ulong GuildId,
    ulong ChannelId,
    ulong ConfiguredByUserId,
    bool IsEnabled,
    IReadOnlySet<GameKey> Games,
    DateTimeOffset UpdatedAtUtc,
    string? GuildName = null,
    string? ChannelName = null,
    double EventStartOffsetHours = 0,
    double EventEndOffsetHours = 48,
    DateTimeOffset? RemovedAtUtc = null,
    IReadOnlySet<GuildTopicSubscription>? TopicSubscriptions = null,
    bool DeleteObsoleteMessages = false);

public sealed record GuildTopicSubscription(GameKey Game, ContentKind Kind);

public static class GuildDestinationTopics
{
    public static IReadOnlySet<GuildTopicSubscription> AllFor(IEnumerable<GameKey> games) =>
        games.SelectMany(game => Enum.GetValues<ContentKind>()
            .Select(kind => new GuildTopicSubscription(game, kind)))
            .ToHashSet();

    public static bool SubscribesTo(this GuildDestination destination, GameKey game, ContentKind kind) =>
        destination.Games.Contains(game) &&
        (destination.TopicSubscriptions is null ||
         destination.TopicSubscriptions.Contains(new GuildTopicSubscription(game, kind)));
}

public static class GuildDestinationGames
{
    public static IReadOnlySet<GameKey> All { get; } = new HashSet<GameKey>
    {
        GameKey.WutheringWaves,
        GameKey.NevernessToEverness,
    };
}

public interface IGuildDestinationStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GuildDestination>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<GuildDestination>> ListActiveAsync(CancellationToken cancellationToken);

    Task ConfigureAsync(
        ulong guildId,
        string guildName,
        ulong channelId,
        string channelName,
        ulong configuredByUserId,
        IReadOnlySet<GameKey> games,
        CancellationToken cancellationToken);

    Task SetEnabledAsync(ulong guildId, bool enabled, CancellationToken cancellationToken);

    Task SetEventNotificationOffsetsAsync(
        ulong guildId,
        double startOffsetHours,
        double endOffsetHours,
        CancellationToken cancellationToken);

    Task SetDeleteObsoleteMessagesAsync(
        ulong guildId,
        bool enabled,
        CancellationToken cancellationToken);

    Task SetTopicSubscriptionsAsync(
        ulong guildId,
        GameKey game,
        IReadOnlySet<ContentKind> kinds,
        CancellationToken cancellationToken);

    Task MarkRemovedAsync(ulong guildId, CancellationToken cancellationToken);

    Task RestoreRemovedAsync(ulong guildId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GuildDestination>> ListRemovedBeforeAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);

    Task<bool> DeleteRemovedAsync(
        ulong guildId,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);

    Task<bool> IsAdministratorAsync(ulong userId, CancellationToken cancellationToken);
}

public sealed record GuildPublicationHistoryItem(
    Guid ContentId,
    ulong GuildId,
    ulong ChannelId,
    string Title,
    GameKey Game,
    string Kind,
    string Purpose,
    string State,
    DateTimeOffset DueAtUtc,
    DateTimeOffset UpdatedAtUtc,
    int AttemptCount,
    string? ProviderMessageId,
    string? LastError);

public interface IGuildPublicationHistoryStore
{
    Task<IReadOnlyList<GuildPublicationHistoryItem>> ListForGuildAsync(
        ulong guildId,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GuildPublicationHistoryItem>> ListPendingForGuildAsync(
        ulong guildId,
        int limit,
        CancellationToken cancellationToken);
}

public sealed record ObsoleteDiscordPublication(
    Guid PublicationId,
    ulong GuildId,
    ulong ChannelId,
    string ProviderMessageId);

public interface IObsoleteDiscordPublicationStore
{
    Task<IReadOnlyList<ObsoleteDiscordPublication>> ListForGuildAsync(
        ulong guildId,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task MarkDeletedAsync(
        IReadOnlyCollection<Guid> publicationIds,
        DateTimeOffset deletedAtUtc,
        CancellationToken cancellationToken);
}

public interface IGuildObsoleteMessageCleanup
{
    Task<int> CleanGuildAsync(ulong guildId, CancellationToken cancellationToken);
}
