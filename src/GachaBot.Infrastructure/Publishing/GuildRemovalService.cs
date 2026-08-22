using GachaBot.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace GachaBot.Infrastructure.Publishing;

public sealed class GuildRemovalService(ISourceDatabaseFactory databaseFactory, TimeProvider timeProvider)
{
    public async Task CancelPendingAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var key in databaseFactory.DatabaseKeys)
        {
            await using var context = databaseFactory.CreateDbContext(key);
            var pending = await context.Publications
                .Where(publication =>
                    publication.DestinationGuildId == checked((long)guildId) &&
                    (publication.State == PublicationState.Pending || publication.State == PublicationState.Processing))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var publication in pending)
            {
                publication.State = PublicationState.Cancelled;
                publication.LastError = "Cancelled because GachaBot was removed from this guild.";
                publication.UpdatedAtUtc = now;
            }

            if (pending.Count > 0)
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task DeleteHistoryAsync(ulong guildId, CancellationToken cancellationToken)
    {
        foreach (var key in databaseFactory.DatabaseKeys)
        {
            await using var context = databaseFactory.CreateDbContext(key);
            var history = await context.Publications
                .Where(publication => publication.DestinationGuildId == checked((long)guildId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            if (history.Count == 0)
            {
                continue;
            }

            context.Publications.RemoveRange(history);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
