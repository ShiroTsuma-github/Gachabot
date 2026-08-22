using GachaBot.Application.Publishing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Workers;

public sealed partial class PublicationDispatcher(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<PublicationDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15), timeProvider);
        do
        {
            try
            {
                await DispatchAvailableAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogIterationFailure(logger, exception);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task DispatchAvailableAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<IPublicationQueueStore>();
            var publisher = scope.ServiceProvider.GetRequiredService<IDiscordPublisher>();
            var leased = await queue.TryLeaseDueAsync(timeProvider.GetUtcNow(), cancellationToken)
                .ConfigureAwait(false);
            if (leased is null)
            {
                return;
            }

            try
            {
                var receipt = await publisher.PublishAsync(leased.Payload, cancellationToken).ConfigureAwait(false);
                await queue.MarkPublishedAsync(leased.PublicationId, receipt, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(900, Math.Pow(2, leased.AttemptCount) * 15));
                await queue.MarkFailedAsync(
                    leased.PublicationId,
                    exception.Message,
                    timeProvider.GetUtcNow() + delay,
                    cancellationToken).ConfigureAwait(false);
                LogPublicationFailure(
                    logger,
                    leased.PublicationId,
                    leased.AttemptCount,
                    exception);
            }
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Error,
        Message = "Publication dispatcher iteration failed.")]
    private static partial void LogIterationFailure(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Discord publication {PublicationId} failed on attempt {Attempt}.")]
    private static partial void LogPublicationFailure(
        ILogger logger,
        Guid publicationId,
        int attempt,
        Exception exception);
}
