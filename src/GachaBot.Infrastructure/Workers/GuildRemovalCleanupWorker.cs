using GachaBot.Application.Publishing;
using GachaBot.Infrastructure.Publishing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Workers;

public sealed partial class GuildRemovalCleanupWorker(
    IGuildDestinationStore destinations,
    GuildRemovalService removalService,
    TimeProvider timeProvider,
    ILogger<GuildRemovalCleanupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1), timeProvider);
        do
        {
            try
            {
                await PurgeExpiredGuildsAsync(stoppingToken).ConfigureAwait(false);
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

    private async Task PurgeExpiredGuildsAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().Subtract(Retention);
        var expired = await destinations.ListRemovedBeforeAsync(cutoff, cancellationToken).ConfigureAwait(false);
        foreach (var guild in expired)
        {
            if (!await destinations.DeleteRemovedAsync(guild.GuildId, cutoff, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await removalService.DeleteHistoryAsync(guild.GuildId, cancellationToken).ConfigureAwait(false);
            LogGuildPurged(logger, guild.GuildId, guild.RemovedAtUtc!.Value);
        }
    }

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information, Message = "Deleted configuration and publication history for guild {GuildId}, removed at {RemovedAtUtc}.")]
    private static partial void LogGuildPurged(ILogger logger, ulong guildId, DateTimeOffset removedAtUtc);

    [LoggerMessage(EventId = 1402, Level = LogLevel.Error, Message = "Guild removal cleanup failed.")]
    private static partial void LogCleanupFailure(ILogger logger, Exception exception);
}
