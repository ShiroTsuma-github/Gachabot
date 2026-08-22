using GachaBot.Application.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Workers;

public sealed partial class ObsoleteDiscordMessageCleanupWorker(
    IGuildDestinationStore destinations,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<ObsoleteDiscordMessageCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        do
        {
            try
            {
                await CleanAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogCleanupFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task CleanAsync(CancellationToken cancellationToken)
    {
        var guildIds = (await destinations.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(destination => destination.DeleteObsoleteMessages)
            .Select(destination => destination.GuildId)
            .ToArray();
        if (guildIds.Length == 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IGuildObsoleteMessageCleanup>();
        foreach (var guildId in guildIds)
        {
            var removed = await cleanup.CleanGuildAsync(guildId, cancellationToken).ConfigureAwait(false);
            if (removed > 0)
            {
                LogMessagesRemoved(logger, removed, guildId);
            }
        }
    }

    [LoggerMessage(EventId = 1502, Level = LogLevel.Information, Message = "Removed {Count} obsolete Discord publications for guild {GuildId}.")]
    private static partial void LogMessagesRemoved(ILogger logger, int count, ulong guildId);

    [LoggerMessage(EventId = 1503, Level = LogLevel.Error, Message = "Obsolete Discord message cleanup failed.")]
    private static partial void LogCleanupFailure(ILogger logger, Exception exception);
}
