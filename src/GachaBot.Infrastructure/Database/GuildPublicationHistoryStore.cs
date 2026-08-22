using GachaBot.Application.Publishing;
using Microsoft.EntityFrameworkCore;

namespace GachaBot.Infrastructure.Database;

public sealed class GuildPublicationHistoryStore(ISourceDatabaseFactory databaseFactory)
    : IGuildPublicationHistoryStore
{
    public async Task<IReadOnlyList<GuildPublicationHistoryItem>> ListForGuildAsync(
        ulong guildId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (guildId == 0 || guildId > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(guildId));
        }

        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "The history limit must be between 1 and 200.");
        }

        var items = new List<GuildPublicationHistoryItem>();
        foreach (var sourceKey in databaseFactory.DatabaseKeys)
        {
            await using var context = databaseFactory.CreateDbContext(sourceKey);
            var rows = await context.Publications.AsNoTracking()
                .Where(publication => publication.DestinationGuildId == checked((long)guildId))
                .OrderByDescending(publication => publication.UpdatedAtUtc)
                .Take(limit)
                .Select(publication => new PublicationHistoryRow(
                    publication.ContentId,
                    publication.DestinationGuildId!.Value,
                    publication.DestinationChannelId!.Value,
                    publication.Content.Title,
                    publication.Content.Game,
                    publication.Content.Kind,
                    publication.Purpose,
                    publication.State,
                    publication.DueAtUtc,
                    publication.UpdatedAtUtc,
                    publication.AttemptCount,
                    publication.ProviderMessageId,
                    publication.LastError))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            items.AddRange(rows.Select(row => new GuildPublicationHistoryItem(
                row.ContentId,
                checked((ulong)row.GuildId),
                checked((ulong)row.ChannelId),
                row.Title,
                row.Game,
                row.Kind.ToString(),
                row.Purpose.ToString(),
                row.State.ToString(),
                row.DueAtUtc,
                row.UpdatedAtUtc,
                row.AttemptCount,
                row.ProviderMessageId,
                row.LastError)));
        }

        return items
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<GuildPublicationHistoryItem>> ListPendingForGuildAsync(
        ulong guildId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (guildId == 0 || guildId > long.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(guildId));
        }

        if (limit is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "The history limit must be between 1 and 200.");
        }

        var items = new List<GuildPublicationHistoryItem>();
        foreach (var sourceKey in databaseFactory.DatabaseKeys)
        {
            await using var context = databaseFactory.CreateDbContext(sourceKey);
            var rows = await context.Publications.AsNoTracking()
                .Where(publication =>
                    publication.DestinationGuildId == checked((long)guildId) &&
                    (publication.State == PublicationState.Pending || publication.State == PublicationState.Processing))
                .OrderBy(publication => publication.DueAtUtc)
                .Take(limit)
                .Select(publication => new PublicationHistoryRow(
                    publication.ContentId,
                    publication.DestinationGuildId!.Value,
                    publication.DestinationChannelId!.Value,
                    publication.Content.Title,
                    publication.Content.Game,
                    publication.Content.Kind,
                    publication.Purpose,
                    publication.State,
                    publication.DueAtUtc,
                    publication.UpdatedAtUtc,
                    publication.AttemptCount,
                    publication.ProviderMessageId,
                    publication.LastError))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            items.AddRange(rows.Select(ToContract));
        }

        return items
            .OrderBy(item => item.DueAtUtc)
            .Take(limit)
            .ToArray();
    }

    private static GuildPublicationHistoryItem ToContract(PublicationHistoryRow row) => new(
        row.ContentId,
        checked((ulong)row.GuildId),
        checked((ulong)row.ChannelId),
        row.Title,
        row.Game,
        row.Kind.ToString(),
        row.Purpose.ToString(),
        row.State.ToString(),
        row.DueAtUtc,
        row.UpdatedAtUtc,
        row.AttemptCount,
        row.ProviderMessageId,
        row.LastError);

    private sealed record PublicationHistoryRow(
        Guid ContentId,
        long GuildId,
        long ChannelId,
        string Title,
        GachaBot.Domain.Games.GameKey Game,
        GachaBot.Domain.Content.ContentKind Kind,
        PublicationPurpose Purpose,
        PublicationState State,
        DateTimeOffset DueAtUtc,
        DateTimeOffset UpdatedAtUtc,
        int AttemptCount,
        string? ProviderMessageId,
        string? LastError);
}
