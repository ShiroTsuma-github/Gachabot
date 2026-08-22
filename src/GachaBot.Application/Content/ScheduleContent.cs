namespace GachaBot.Application.Content;

public sealed record ScheduleContentCommand(Guid ContentId, DateTimeOffset PublishAt);

public interface IContentScheduleStore
{
    Task ScheduleAsync(
        Guid contentId,
        DateTimeOffset publishAtUtc,
        CancellationToken cancellationToken);
}

public sealed class ScheduleContentHandler(IContentScheduleStore store)
{
    public Task ExecuteAsync(
        ScheduleContentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.ContentId == Guid.Empty)
        {
            throw new ArgumentException("Content id is required.", nameof(command));
        }

        return store.ScheduleAsync(command.ContentId, command.PublishAt.ToUniversalTime(), cancellationToken);
    }
}
