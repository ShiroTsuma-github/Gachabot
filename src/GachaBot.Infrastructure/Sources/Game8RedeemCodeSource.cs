using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using GachaBot.Application.Ingestion;
using GachaBot.Domain.Content;
using GachaBot.Domain.Games;

namespace GachaBot.Infrastructure.Sources;

public sealed partial class Game8RedeemCodeSource(
    HttpClient httpClient,
    GameKey game,
    Uri pageUri,
    SourceTrust trust = SourceTrust.ReviewRequired) : IGameContentSource
{
    public string Key => $"game8-{game.ToSlug()}-redeem-codes";

    public SourceTrust Trust { get; } = trust;

    public async IAsyncEnumerable<SourceContentSnapshot> FetchAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
        request.Headers.UserAgent.ParseAdd("GachaBot/1.0 (+Discord update monitor)");
        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var document = await new HtmlParser().ParseDocumentAsync(html, cancellationToken).ConfigureAwait(false);
        var candidates = FindActiveCodeCandidates(document);

        foreach (var value in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var code = RedeemCode.Create(game, value, pageUri);
            yield return new SourceContentSnapshot(
                Key,
                code.Code,
                game,
                ContentKind.RedeemCode,
                $"New redeem code: {code.Code}",
                pageUri,
                ContentDocument.Create(
                [
                    new HeadingBlock(code.Code, 1),
                    new TextBlock("Candidate redeem code found by Game8. Verify it before publication.", 2),
                    new LinkBlock("Source and redemption details", pageUri, 3),
                ]),
                null,
                code.ExpiresAtUtc);
        }
    }

    private static string[] FindActiveCodeCandidates(IDocument document)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var heading in document.QuerySelectorAll("h2, h3"))
        {
            if (!heading.TextContent.Contains("active", StringComparison.OrdinalIgnoreCase) ||
                !heading.TextContent.Contains("code", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (var sibling = heading.NextElementSibling;
                 sibling is not null && sibling.TagName is not "H2" and not "H3";
                 sibling = sibling.NextElementSibling)
            {
                foreach (var codeElement in sibling.QuerySelectorAll("code"))
                {
                    var normalized = SourceParsing.NormalizeWhitespace(codeElement.TextContent).ToUpperInvariant();
                    if (RedeemCodeRegex().IsMatch(normalized))
                    {
                        result.Add(normalized);
                    }
                }
            }
        }

        return result.Order(StringComparer.Ordinal).ToArray();
    }

    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{4,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex RedeemCodeRegex();
}
