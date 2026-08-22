using GachaBot.Application.Ingestion;
using GachaBot.Infrastructure.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GachaBot.Infrastructure.Workers;

public sealed partial class SourcePollingWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<SourcePollingOptions> options,
    TimeProvider timeProvider,
    ILogger<SourcePollingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        using var timer = new PeriodicTimer(
            TimeSpan.FromMinutes(options.Value.IntervalMinutes),
            timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var operations = scope.ServiceProvider.GetRequiredService<ISourceOperations>();
            var results = await operations.RunAllAsync(cancellationToken).ConfigureAwait(false);
            var failed = results.Count(result => !result.Succeeded);
            LogRunCompleted(logger, results.Count, failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogPollingFailure(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Information,
        Message = "Source polling completed for {SourceCount} sources; {FailureCount} failed.")]
    private static partial void LogRunCompleted(ILogger logger, int sourceCount, int failureCount);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Error,
        Message = "Source polling iteration failed.")]
    private static partial void LogPollingFailure(ILogger logger, Exception exception);
}
