using System.Text.Json;
using GachaBot.Application.Ingestion;
using GachaBot.Domain.Content;
using GachaBot.Domain.Games;
using Microsoft.Extensions.Configuration;

namespace GachaBot.Infrastructure.Sources;

public sealed class WutheringWavesOfficialSource(
    HttpClient httpClient,
    IConfiguration? configuration = null) : IGameContentSource
{
    public const string SourceKey = "official-wuthering-waves";

    private static readonly Uri FeedUri = new(
        "https://hw-media-cdn-mingchao.kurogame.com/akiwebsite/website2.0/json/G152/en/ArticleMenu.json");

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
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        foreach (var article in document.RootElement.EnumerateArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetRequired(article, "articleId", out var id) ||
                !TryGetRequired(article, "articleTitle", out var title))
            {
                continue;
            }

            var articleUri = new Uri($"https://wutheringwaves.kurogames.com/en/main/news/detail/{id}");
            var html = GetString(article, "articleContent") ?? GetString(article, "articleDesc") ?? string.Empty;
            var blocks = (await SourceParsing.HtmlToBlocksAsync(
                html,
                articleUri,
                title,
                cancellationToken).ConfigureAwait(false)).ToList();
            var cover = GetString(article, "suggestCover");
            if (SourceParsing.TryResolveHttpUri(FeedUri, cover, out var coverUri) &&
                blocks.All(block => block is not ImageBlock image || image.Url != coverUri))
            {
                blocks.Add(new ImageBlock(coverUri, title, blocks.Count + 1));
            }

            yield return new SourceContentSnapshot(
                SourceKey,
                id,
                GameKey.WutheringWaves,
                SourceParsing.Classify(title),
                title,
                articleUri,
                ContentDocument.Create(blocks),
                SourceParsing.ParseUtcPlusEight(GetString(article, "startTime")),
                null);
        }
    }

    private static bool TryGetRequired(JsonElement element, string property, out string value)
    {
        if (element.TryGetProperty(property, out var found))
        {
            value = found.ValueKind == JsonValueKind.String
                ? found.GetString() ?? string.Empty
                : found.ToString();
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;
}
