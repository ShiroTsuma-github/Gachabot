using GachaBot.Application.Content;
using GachaBot.Application.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Workers;

public sealed partial class ArchiveWorker(
    IServiceScopeFactory scopeFactory,
    IGuildDestinationStore destinations,
    TimeProvider timeProvider,
    ILogger<ArchiveWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), timeProvider);
        do
        {
            try
            {
                await ArchiveAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogArchiveFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task ArchiveAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IContentManagementStore>();
        var count = await store.ArchiveExpiredAsync(timeProvider.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);
        if (count > 0)
        {
            await CleanArchivedMessagesAsync(cancellationToken).ConfigureAwait(false);
            LogArchived(logger, count);
        }
    }

    private async Task CleanArchivedMessagesAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetService<IGuildObsoleteMessageCleanup>();
        if (cleanup is null)
        {
            return;
        }

        var guildIds = (await destinations.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(destination => destination.DeleteObsoleteMessages)
            .Select(destination => destination.GuildId)
            .ToArray();
        foreach (var guildId in guildIds)
        {
            await cleanup.CleanGuildAsync(guildId, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(
        EventId = 2201,
        Level = LogLevel.Information,
        Message = "Archived {Count} expired or retained content items.")]
    private static partial void LogArchived(ILogger logger, int count);

    [LoggerMessage(
        EventId = 2202,
        Level = LogLevel.Error,
        Message = "Automatic archiving iteration failed.")]
    private static partial void LogArchiveFailure(ILogger logger, Exception exception);
}
