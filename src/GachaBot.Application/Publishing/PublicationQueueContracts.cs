namespace GachaBot.Application.Publishing;

public sealed record LeasedPublication(
    Guid PublicationId,
    PublicationPayload Payload,
    int AttemptCount);

public interface IPublicationQueueStore
{
    Task<LeasedPublication?> TryLeaseDueAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken);

    Task MarkPublishedAsync(
        Guid publicationId,
        PublishReceipt receipt,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(
        Guid publicationId,
        string failureMessage,
        DateTimeOffset retryAtUtc,
        CancellationToken cancellationToken);
}
