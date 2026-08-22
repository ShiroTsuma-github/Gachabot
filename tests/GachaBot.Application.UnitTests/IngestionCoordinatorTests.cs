using GachaBot.Application.Ingestion;
using GachaBot.Domain.Content;
using GachaBot.Domain.Games;

namespace GachaBot.Application.UnitTests;

public sealed class IngestionCoordinatorTests
{
    [Fact]
    public async Task RunAsync_FirstSuccessfulRun_SeedsBaselineWithoutPublication()
    {
        var source = new StubSource(SourceTrust.Official, SampleSnapshot());
        var state = new StubStateStore(hasBaseline: false);
        var sink = new CapturingSink();
        var coordinator = new IngestionCoordinator(state, sink);

        var result = await coordinator.RunAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Seen);
        Assert.Equal(PublicationDisposition.SuppressBaseline, sink.Dispositions.Single());
        Assert.True(state.Completed);
    }

    [Fact]
    public async Task RunAsync_AfterBaseline_ForOfficialSource_AllowsAutomaticPublication()
    {
        var source = new StubSource(SourceTrust.Official, SampleSnapshot());
        var sink = new CapturingSink();
        var coordinator = new IngestionCoordinator(new StubStateStore(hasBaseline: true), sink);

        await coordinator.RunAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(PublicationDisposition.AutoPublish, sink.Dispositions.Single());
    }

    [Fact]
    public async Task RunAsync_ForReviewSource_RequiresApproval()
    {
        var source = new StubSource(SourceTrust.ReviewRequired, SampleSnapshot());
        var sink = new CapturingSink();
        var coordinator = new IngestionCoordinator(new StubStateStore(hasBaseline: true), sink);

        await coordinator.RunAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(PublicationDisposition.AwaitReview, sink.Dispositions.Single());
    }

    [Fact]
    public async Task RunAsync_FirstRunForReviewSource_CreatesReviewCandidate()
    {
        var source = new StubSource(SourceTrust.ReviewRequired, SampleSnapshot());
        var sink = new CapturingSink();
        var coordinator = new IngestionCoordinator(new StubStateStore(hasBaseline: false), sink);

        await coordinator.RunAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(PublicationDisposition.AwaitReview, sink.Dispositions.Single());
    }

    [Fact]
    public async Task RunAsync_FirstRunForTimelineSource_SchedulesUpcomingEvents()
    {
        var source = new StubSource(SourceTrust.Trusted, schedulesUpcomingEvents: true, SampleSnapshot());
        var sink = new CapturingSink();
        var coordinator = new IngestionCoordinator(new StubStateStore(hasBaseline: false), sink);

        await coordinator.RunAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(PublicationDisposition.ScheduleUpcoming, sink.Dispositions.Single());
    }

    [Fact]
    public async Task RunAsync_AfterBaseline_ForTimelineSource_StillSchedulesByEventStart()
    {
        var source = new StubSource(SourceTrust.Trusted, schedulesUpcomingEvents: true, SampleSnapshot());
        var sink = new CapturingSink();
        var coordinator = new IngestionCoordinator(new StubStateStore(hasBaseline: true), sink);

        await coordinator.RunAsync(source, TestContext.Current.CancellationToken);

        Assert.Equal(PublicationDisposition.ScheduleUpcoming, sink.Dispositions.Single());
    }

    [Fact]
    public async Task RunAsync_WhenSourceFails_RecordsFailureAndRethrows()
    {
        var state = new StubStateStore(hasBaseline: true);
        var coordinator = new IngestionCoordinator(state, new CapturingSink());

        await Assert.ThrowsAsync<HttpRequestException>(() => coordinator.RunAsync(
            new ThrowingSource(),
            TestContext.Current.CancellationToken));

        Assert.True(state.Failed);
        Assert.Contains("403", state.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_LargeSource_WritesBoundedBatchesInsteadOfPerItemUpserts()
    {
        var snapshots = Enumerable.Range(1, 124)
            .Select(index => SampleSnapshot() with { ExternalId = index.ToString(CultureInfo.InvariantCulture) })
            .ToArray();
        var sink = new CapturingSink();
        var coordinator = new IngestionCoordinator(new StubStateStore(hasBaseline: false), sink);

        var result = await coordinator.RunAsync(
            new StubSource(SourceTrust.Official, snapshots),
            TestContext.Current.CancellationToken);

        Assert.Equal(124, result.Created);
        Assert.Equal([50, 50, 24], sink.BatchSizes);
        Assert.Equal(0, sink.SingleCalls);
    }

    private static SourceContentSnapshot SampleSnapshot() => new(
        "official-wuwa",
        "article-42",
        GameKey.WutheringWaves,
        ContentKind.News,
        "Patch notes",
        new Uri("https://example.com/42"),
        ContentDocument.Create([new TextBlock("Details", 1)]),
        DateTimeOffset.Parse("2026-08-13T08:00:00Z", CultureInfo.InvariantCulture),
        null);

    private sealed class StubSource(SourceTrust trust, params SourceContentSnapshot[] snapshots)
        : IGameContentSource
    {
        private readonly bool _schedulesUpcomingEvents;

        public StubSource(
            SourceTrust trust,
            bool schedulesUpcomingEvents,
            params SourceContentSnapshot[] snapshots)
            : this(trust, snapshots) => _schedulesUpcomingEvents = schedulesUpcomingEvents;

        public string Key => "official-wuwa";

        public SourceTrust Trust => trust;

        public bool SchedulesUpcomingEvents => _schedulesUpcomingEvents;

        public async IAsyncEnumerable<SourceContentSnapshot> FetchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var snapshot in snapshots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return snapshot;
                await Task.Yield();
            }
        }
    }

    private sealed class StubStateStore(bool hasBaseline) : ISourceStateStore
    {
        public bool Completed { get; private set; }

        public bool Failed { get; private set; }

        public string FailureMessage { get; private set; } = string.Empty;

        public Task<bool> HasCompletedBaselineAsync(string sourceKey, CancellationToken cancellationToken) =>
            Task.FromResult(hasBaseline);

        public Task MarkSucceededAsync(
            string sourceKey,
            DateTimeOffset completedAtUtc,
            CancellationToken cancellationToken)
        {
            Completed = true;
            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(
            string sourceKey,
            DateTimeOffset attemptedAtUtc,
            string failureMessage,
            CancellationToken cancellationToken)
        {
            Failed = true;
            FailureMessage = failureMessage;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSource : IGameContentSource
    {
        public string Key => "failing-source";

        public SourceTrust Trust => SourceTrust.Official;

        public async IAsyncEnumerable<SourceContentSnapshot> FetchAsync(
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new HttpRequestException("403 Forbidden");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class CapturingSink : IIngestionSink
    {
        public List<PublicationDisposition> Dispositions { get; } = [];

        public List<int> BatchSizes { get; } = [];

        public int SingleCalls { get; private set; }

        public Task<ContentUpsertOutcome> UpsertAsync(
            SourceContentSnapshot snapshot,
            PublicationDisposition disposition,
            CancellationToken cancellationToken)
        {
            SingleCalls++;
            Dispositions.Add(disposition);
            return Task.FromResult(ContentUpsertOutcome.Created);
        }

        public Task<IReadOnlyList<ContentUpsertOutcome>> UpsertBatchAsync(
            IReadOnlyList<SourceContentSnapshot> snapshots,
            PublicationDisposition disposition,
            CancellationToken cancellationToken)
        {
            BatchSizes.Add(snapshots.Count);
            Dispositions.AddRange(Enumerable.Repeat(disposition, snapshots.Count));
            return Task.FromResult<IReadOnlyList<ContentUpsertOutcome>>(
                snapshots.Select(_ => ContentUpsertOutcome.Created).ToArray());
        }
    }
}
