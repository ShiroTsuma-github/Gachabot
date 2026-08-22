using GachaBot.Application.Publishing;
using Microsoft.EntityFrameworkCore;

namespace GachaBot.Infrastructure.Database;

public sealed class CompositePublicationQueueStore(
    ISourceDatabaseFactory databaseFactory,
    IGuildDestinationStore destinations)
    : IPublicationQueueStore
{
    public async Task<LeasedPublication?> TryLeaseDueAsync(
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        string? selectedKey = null;
        DateTimeOffset? selectedContentDate = null;
        DateTimeOffset? selectedDueAt = null;
        DateTimeOffset? selectedCreatedAt = null;
        foreach (var key in databaseFactory.DatabaseKeys)
        {
            await using var context = databaseFactory.CreateDbContext(key);
            var next = await context.Publications.AsNoTracking()
                .Where(item =>
                    (item.State == PublicationState.Pending && item.DueAtUtc <= nowUtc) ||
                    (item.State == PublicationState.Processing && item.UpdatedAtUtc <= nowUtc.AddMinutes(-5)))
                .OrderBy(item => item.Content.SourcePublishedAtUtc ?? item.Content.CreatedAtUtc)
                .ThenBy(item => item.DueAtUtc)
                .ThenBy(item => item.CreatedAtUtc)
                .Select(item => new NextPublicationOrder(
                    item.Content.SourcePublishedAtUtc ?? item.Content.CreatedAtUtc,
                    item.DueAtUtc,
                    item.CreatedAtUtc))
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (next is not null && IsEarlier(next, selectedContentDate, selectedDueAt, selectedCreatedAt))
            {
                selectedKey = key;
                selectedContentDate = next.ContentDateUtc;
                selectedDueAt = next.DueAtUtc;
                selectedCreatedAt = next.CreatedAtUtc;
            }
        }

        if (selectedKey is null)
        {
            return null;
        }

        await using var selectedContext = databaseFactory.CreateDbContext(selectedKey);
        return await new PublicationQueueStore(selectedContext, destinations)
            .TryLeaseDueAsync(nowUtc, cancellationToken).ConfigureAwait(false);
    }

    public async Task MarkPublishedAsync(
        Guid publicationId,
        PublishReceipt receipt,
        CancellationToken cancellationToken) => await WithPublicationStoreAsync(
        publicationId,
        store => store.MarkPublishedAsync(publicationId, receipt, cancellationToken),
        cancellationToken).ConfigureAwait(false);

    public async Task MarkFailedAsync(
        Guid publicationId,
        string failureMessage,
        DateTimeOffset retryAtUtc,
        CancellationToken cancellationToken) => await WithPublicationStoreAsync(
        publicationId,
        store => store.MarkFailedAsync(publicationId, failureMessage, retryAtUtc, cancellationToken),
        cancellationToken).ConfigureAwait(false);

    private async Task WithPublicationStoreAsync(
        Guid publicationId,
        Func<PublicationQueueStore, Task> operation,
        CancellationToken cancellationToken)
    {
        foreach (var key in databaseFactory.DatabaseKeys)
        {
            await using var context = databaseFactory.CreateDbContext(key);
            if (!await context.Publications.AnyAsync(
                    publication => publication.Id == publicationId,
                    cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await operation(new PublicationQueueStore(context, destinations)).ConfigureAwait(false);
            return;
        }

        throw new KeyNotFoundException($"Publication '{publicationId}' was not found.");
    }

    private static bool IsEarlier(
        NextPublicationOrder candidate,
        DateTimeOffset? selectedContentDate,
        DateTimeOffset? selectedDueAt,
        DateTimeOffset? selectedCreatedAt) =>
        selectedContentDate is null ||
        candidate.ContentDateUtc < selectedContentDate ||
        candidate.ContentDateUtc == selectedContentDate &&
        (candidate.DueAtUtc < selectedDueAt ||
         candidate.DueAtUtc == selectedDueAt && candidate.CreatedAtUtc < selectedCreatedAt);

    private sealed record NextPublicationOrder(
        DateTimeOffset ContentDateUtc,
        DateTimeOffset DueAtUtc,
        DateTimeOffset CreatedAtUtc);
}
