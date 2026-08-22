using System.Globalization;
using System.Text.Json;
using GachaBot.Application.Ingestion;
using GachaBot.Domain.Content;

namespace GachaBot.Infrastructure.Sources;

public sealed class JsonArticleFeedHandler(
    HttpClient httpClient,
    ISourceContentLookup contentLookup) : ISourceHandler
{
    public const string HandlerKey = "json-article-feed";

    public string Key => HandlerKey;

    public async IAsyncEnumerable<SourceContentSnapshot> FetchAsync(
        SourceDefinition definition,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rules = definition.JsonArticle
            ?? throw new InvalidOperationException($"Source '{definition.Key}' has no JsonArticle rules.");
        using var listDocument = await GetJsonAsync(definition.Url, cancellationToken).ConfigureAwait(false);
        var candidates = EnumerateItems(listDocument.RootElement)
            .Select((item, index) => new ListCandidate(
                item,
                GetRequiredValue(item, rules.ExternalIdField),
                ParsePublishedAt(item, item, rules),
                index))
            .DistinctBy(candidate => candidate.ExternalId, StringComparer.Ordinal)
            .OrderBy(candidate => candidate.PublishedAtUtc is null)
            .ThenByDescending(candidate => candidate.PublishedAtUtc)
            .ThenBy(candidate => candidate.OriginalIndex)
            .ToArray();
        var states = await contentLookup.GetContentStatesAsync(
                definition.Key,
                candidates.Select(candidate => candidate.ExternalId).ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var listItem = candidate.Item;
            var externalId = candidate.ExternalId;
            if (states.TryGetValue(externalId, out var state) && state.Exists && !state.NeedsRefresh)
            {
                if (rules.StopWhenKnown)
                {
                    yield break;
                }

                continue;
            }

            var detailUri = ExpandUri(rules.DetailUrlTemplate, externalId);
            using var detailDocument = await GetJsonAsync(detailUri, cancellationToken).ConfigureAwait(false);
            var detail = detailDocument.RootElement;
            var title = GetOptionalValue(detail, rules.TitleField)
                ?? GetRequiredValue(listItem, rules.TitleField);
            var articleUri = ExpandUri(
                definition.ArticleUrlTemplate ?? definition.Url.AbsoluteUri,
                externalId);
            var html = GetOptionalValue(detail, rules.ContentField)
                ?? GetOptionalValue(listItem, rules.ContentField)
                ?? string.Empty;
            var blocks = (await SourceParsing.HtmlToBlocksAsync(
                html,
                articleUri,
                title,
                cancellationToken).ConfigureAwait(false)).ToList();
            var cover = GetOptionalValue(detail, rules.CoverField)
                ?? GetOptionalValue(listItem, rules.CoverField);
            if (SourceParsing.TryResolveHttpUri(detailUri, cover, out var coverUri) &&
                blocks.All(block => block is not ImageBlock image || image.Url != coverUri))
            {
                blocks.Add(new ImageBlock(coverUri, title, blocks.Count + 1));
            }

            blocks.Add(new LinkBlock("Official announcement", articleUri, blocks.Count + 1));
            yield return new SourceContentSnapshot(
                definition.Key,
                externalId,
                definition.Game,
                SourceParsing.Classify(title),
                title,
                articleUri,
                ContentDocument.Create(blocks),
                ParsePublishedAt(detail, listItem, rules),
                null);
        }
    }

    private sealed record ListCandidate(
        JsonElement Item,
        string ExternalId,
        DateTimeOffset? PublishedAtUtc,
        int OriginalIndex);

    private async Task<JsonDocument> GetJsonAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static IEnumerable<JsonElement> EnumerateItems(JsonElement root) => root.ValueKind switch
    {
        JsonValueKind.Array => root.EnumerateArray(),
        JsonValueKind.Object => [root],
        _ => throw new InvalidOperationException("JSON article feed must contain an object or an array."),
    };

    private static string GetRequiredValue(JsonElement element, string field) =>
        GetOptionalValue(element, field)
        ?? throw new InvalidOperationException($"Required JSON field '{field}' is missing.");

    private static string? GetOptionalValue(JsonElement element, string? field)
    {
        if (string.IsNullOrWhiteSpace(field) ||
            !element.TryGetProperty(field, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var result = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private static Uri ExpandUri(string template, string externalId) =>
        new(template.Replace("{externalId}", Uri.EscapeDataString(externalId), StringComparison.Ordinal));

    private static DateTimeOffset? ParsePublishedAt(
        JsonElement detail,
        JsonElement listItem,
        JsonArticleRules rules)
    {
        var value = GetOptionalValue(detail, rules.PublishedAtField)
            ?? GetOptionalValue(listItem, rules.PublishedAtField);
        if (value is null || string.IsNullOrWhiteSpace(rules.PublishedAtFormat) ||
            !DateTime.TryParseExact(
                value,
                rules.PublishedAtFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return null;
        }

        return new DateTimeOffset(parsed, TimeSpan.FromHours(rules.PublishedAtUtcOffsetHours)).ToUniversalTime();
    }
}
