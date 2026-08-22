using GachaBot.Application.Publishing;
using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Publishing;

public sealed partial class GuildObsoleteMessageCleanup(
    IGuildDestinationStore destinations,
    IObsoleteDiscordPublicationStore publications,
    IDiscordPublisher publisher,
    TimeProvider timeProvider,
    ILogger<GuildObsoleteMessageCleanup> logger) : IGuildObsoleteMessageCleanup
{
    public async Task<int> CleanGuildAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var destination = (await destinations.ListAsync(cancellationToken).ConfigureAwait(false))
            .SingleOrDefault(item => item.GuildId == guildId && item.DeleteObsoleteMessages);
        if (destination is null)
        {
            return 0;
        }

        var now = timeProvider.GetUtcNow();
        var obsolete = await publications.ListForGuildAsync(guildId, now, cancellationToken).ConfigureAwait(false);
        var deleted = 0;
        foreach (var publication in obsolete)
        {
            try
            {
                await publisher.DeleteMessagesAsync(
                    destination with { ChannelId = publication.ChannelId },
                    publication.ProviderMessageId,
                    cancellationToken).ConfigureAwait(false);
                await publications.MarkDeletedAsync([publication.PublicationId], now, cancellationToken).ConfigureAwait(false);
                deleted++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogMessageCleanupFailure(logger, guildId, publication.PublicationId, exception);
            }
        }

        return deleted;
    }

    [LoggerMessage(
        EventId = 1501,
        Level = LogLevel.Warning,
        Message = "Could not remove obsolete Discord publication {PublicationId} for guild {GuildId}.")]
    private static partial void LogMessageCleanupFailure(
        ILogger logger,
        ulong guildId,
        Guid publicationId,
        Exception exception);
}
