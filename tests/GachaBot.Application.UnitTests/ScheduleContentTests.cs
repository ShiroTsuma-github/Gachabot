using GachaBot.Application.Content;

namespace GachaBot.Application.UnitTests;

public sealed class ScheduleContentTests
{
    [Fact]
    public async Task ExecuteAsync_WithFutureDate_StoresScheduleInUtc()
    {
        var contentId = Guid.NewGuid();
        var store = new CapturingScheduleStore();
        var handler = new ScheduleContentHandler(store);
        var publishAt = DateTimeOffset.Parse("2026-08-14T18:30:00+02:00", CultureInfo.InvariantCulture);

        await handler.ExecuteAsync(
            new ScheduleContentCommand(contentId, publishAt),
            TestContext.Current.CancellationToken);

        Assert.Equal(contentId, store.ContentId);
        Assert.Equal(DateTimeOffset.Parse("2026-08-14T16:30:00Z", CultureInfo.InvariantCulture), store.PublishAtUtc);
    }

    private sealed class CapturingScheduleStore : IContentScheduleStore
    {
        public Guid ContentId { get; private set; }

        public DateTimeOffset PublishAtUtc { get; private set; }

        public Task ScheduleAsync(
            Guid contentId,
            DateTimeOffset publishAtUtc,
            CancellationToken cancellationToken)
        {
            ContentId = contentId;
            PublishAtUtc = publishAtUtc;
            return Task.CompletedTask;
        }
    }
}
