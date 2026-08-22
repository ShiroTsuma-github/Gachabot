using GachaBot.Application.Content;
using GachaBot.Application.Media;
using GachaBot.Application.Publishing;
using Microsoft.Extensions.DependencyInjection;

namespace GachaBot.Infrastructure.Content;

public sealed class ContentDeletionService(
    IContentManagementStore contentStore,
    IServiceProvider serviceProvider,
    IMediaGarbageCollector mediaGarbageCollector) : IContentDeletionService
{
    public async Task DeleteAsync(Guid contentId, CancellationToken cancellationToken)
    {
        var messageIds = await contentStore.GetPublishedMessageIdsAsync(contentId, cancellationToken)
            .ConfigureAwait(false);
        var publisher = serviceProvider.GetService<IDiscordPublisher>();
        if (publisher is not null)
        {
            foreach (var message in messageIds)
            {
                await publisher.DeleteMessagesAsync(
                    new GuildDestination(
                        message.GuildId,
                        message.ChannelId,
                        0,
                        true,
                        GuildDestinationGames.All,
                        DateTimeOffset.MinValue),
                    message.ProviderMessageId,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        else if (messageIds.Count > 0)
        {
            throw new InvalidOperationException("Discord is disabled; published messages must be removed before deleting this content.");
        }

        await contentStore.DeleteAsync(contentId, cancellationToken).ConfigureAwait(false);
        await mediaGarbageCollector.CollectAsync(apply: true, cancellationToken).ConfigureAwait(false);
    }
}
