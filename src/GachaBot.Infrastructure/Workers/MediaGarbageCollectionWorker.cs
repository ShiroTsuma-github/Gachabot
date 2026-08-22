using GachaBot.Application.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Workers;

public sealed partial class MediaGarbageCollectionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<MediaGarbageCollectionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IMediaGarbageCollector>()
                    .CollectAsync(apply: true, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogFailure(logger, exception);
            }
        }
    }

    [LoggerMessage(EventId = 3021, Level = LogLevel.Error, Message = "Media garbage collection failed.")]
    private static partial void LogFailure(ILogger logger, Exception exception);
}
