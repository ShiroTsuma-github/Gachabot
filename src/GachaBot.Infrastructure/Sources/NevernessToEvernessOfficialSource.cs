using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using GachaBot.Application.Ingestion;
using GachaBot.Domain.Content;
using GachaBot.Domain.Games;
using Microsoft.Extensions.Configuration;

namespace GachaBot.Infrastructure.Sources;

public sealed partial class NevernessToEvernessOfficialSource(
    HttpClient httpClient,
    IConfiguration? configuration = null) : IGameContentSource
{
    public const string SourceKey = "official-neverness-to-everness";

    private static readonly Uri FeedUri = new("https://nte.perfectworld.com/en/article/news/index.html");

    public string Key => SourceKey;

    public SourceTrust Trust => SourceTrustConfiguration.Resolve(
        configuration,
        SourceKey,
        SourceTrust.Official);

    public async IAsyncEnumerable<SourceContentSnapshot> FetchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            FeedUri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var document = await new HtmlParser().ParseDocumentAsync(html, cancellationToken).ConfigureAwait(false);

        foreach (var anchor in document.QuerySelectorAll(".listNews > a[href]"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var href = anchor.GetAttribute("href");
            if (!SourceParsing.TryResolveHttpUri(FeedUri, href, out var articleUri))
            {
                continue;
            }

            var title = SourceParsing.NormalizeWhitespace(anchor.QuerySelector(".title")?.TextContent ?? string.Empty);
            var category = SourceParsing.NormalizeWhitespace(anchor.QuerySelector(".type")?.TextContent ?? string.Empty);
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var match = ArticleIdRegex().Match(articleUri.AbsolutePath);
            var externalId = match.Success ? match.Groups[1].Value : articleUri.AbsolutePath;
            var dateText = anchor.QuerySelector(".date")?.TextContent?.Trim();
            DateTimeOffset? publishedAtUtc = DateTime.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var date)
                ? new DateTimeOffset(DateTime.SpecifyKind(date, DateTimeKind.Utc))
                : null;
            var content = string.IsNullOrWhiteSpace(category)
                ? $"Read the official announcement: {title}"
                : $"{category}: {title}";

            yield return new SourceContentSnapshot(
                SourceKey,
                externalId,
                GameKey.NevernessToEverness,
                SourceParsing.Classify(title, category),
                title,
                articleUri,
                ContentDocument.Create(
                [
                    new TextBlock(content, 1),
                    new LinkBlock("Official announcement", articleUri, 2),
                ]),
                publishedAtUtc,
                null);
        }
    }

    [GeneratedRegex(@"/(\d+)\.html$")]
    private static partial Regex ArticleIdRegex();
}
