using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Sources;

public sealed partial class PagedHtmlArticleFeedHandler
{
    [LoggerMessage(
        EventId = 2101,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: starting paged scan at page {FirstPageNumber}, with a limit of {MaximumPages} pages.")]
    private static partial void LogScanStarted(
        ILogger logger,
        string sourceKey,
        int firstPageNumber,
        int maximumPages);

    [LoggerMessage(
        EventId = 2102,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: fetching page {PageNumber} from {PageUri}.")]
    private static partial void LogPageFetchStarted(
        ILogger logger,
        string sourceKey,
        int pageNumber,
        Uri pageUri);

    [LoggerMessage(
        EventId = 2103,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: page {PageNumber} contains {CandidateCount} candidates; {PreparedItems} items prepared so far.")]
    private static partial void LogPageCandidatesFound(
        ILogger logger,
        string sourceKey,
        int pageNumber,
        int candidateCount,
        int preparedItems);

    [LoggerMessage(
        EventId = 2104,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: fetching item {ItemNumber} on page {PageNumber}: {ExternalId} ({Title}).")]
    private static partial void LogItemFetchStarted(
        ILogger logger,
        string sourceKey,
        int itemNumber,
        int pageNumber,
        string externalId,
        string title);

    [LoggerMessage(
        EventId = 2105,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: prepared item {ItemNumber}: {ExternalId} ({Title}).")]
    private static partial void LogItemPrepared(
        ILogger logger,
        string sourceKey,
        int itemNumber,
        string externalId,
        string title);

    [LoggerMessage(
        EventId = 2110,
        Level = LogLevel.Warning,
        Message = "Source {SourceKey}: skipping missing article {ExternalId} at {ArticleUri}.")]
    private static partial void LogMissingArticleSkipped(
        ILogger logger,
        string sourceKey,
        string externalId,
        Uri articleUri);

    [LoggerMessage(
        EventId = 2106,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: page {PageNumber} was not found; scan completed after {PreparedItems} items.")]
    private static partial void LogMissingPageStoppedScan(
        ILogger logger,
        string sourceKey,
        int pageNumber,
        int preparedItems);

    [LoggerMessage(
        EventId = 2107,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: page {PageNumber} contains no candidates; scan completed after {PreparedItems} items.")]
    private static partial void LogEmptyPageStoppedScan(
        ILogger logger,
        string sourceKey,
        int pageNumber,
        int preparedItems);

    [LoggerMessage(
        EventId = 2108,
        Level = LogLevel.Information,
        Message = "Source {SourceKey}: known item {ExternalId} on page {PageNumber} stopped the scan after {PreparedItems} prepared items.")]
    private static partial void LogKnownItemStoppedScan(
        ILogger logger,
        string sourceKey,
        string externalId,
        int pageNumber,
        int preparedItems);

    [LoggerMessage(
        EventId = 2109,
        Level = LogLevel.Warning,
        Message = "Source {SourceKey}: reached the configured limit of {MaximumPages} pages after preparing {PreparedItems} items.")]
    private static partial void LogPageLimitReached(
        ILogger logger,
        string sourceKey,
        int maximumPages,
        int preparedItems);
}
