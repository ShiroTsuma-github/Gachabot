using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GachaBot.Application.Ingestion;
using GachaBot.Domain.Content;

namespace GachaBot.Infrastructure.Sources;

/// <summary>
/// Reads the event model embedded in WuWa Tracker's Next.js server payload.
/// The tracker is the schedule source; every item retains the linked official Kuro announcement.
/// </summary>
public sealed partial class WutheringWavesTimelineHandler(HttpClient httpClient) : ISourceHandler
{
    public const string HandlerKey = "wuthering-waves-timeline";

    public string Key => HandlerKey;

    public async IAsyncEnumerable<SourceContentSnapshot> FetchAsync(
        SourceDefinition definition,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rules = definition.Timeline
            ?? throw new InvalidOperationException($"Source '{definition.Key}' has no Timeline rules.");
        var html = await httpClient.GetStringAsync(definition.Url, cancellationToken).ConfigureAwait(false);
        var payload = FindTimelinePayload(html, definition.Url);
        foreach (var item in ReadEvents(payload, definition, rules))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }

    private static JsonElement FindTimelinePayload(string html, Uri sourceUrl)
    {
        foreach (Match match in NextPayloadPattern().Matches(html))
        {
            using var chunk = JsonDocument.Parse(match.Groups["chunk"].Value);
            var value = chunk.RootElement[1].GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var objectStart = value.IndexOf('{');
            if (objectStart < 0 ||
                !value.AsSpan(objectStart).StartsWith("{\"banners\":".AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            using var parsed = JsonDocument.Parse(value[objectStart..]);
            return parsed.RootElement.Clone();
        }

        throw new InvalidOperationException(
            $"Timeline source '{sourceUrl}' did not contain the expected event payload.");
    }

    private static SourceContentSnapshot[] ReadEvents(
        JsonElement payload,
        SourceDefinition definition,
        TimelineRules rules)
    {
        var items = new List<SourceContentSnapshot>();
        foreach (var collectionName in new[] { "banners", "activities" })
        {
            if (!payload.TryGetProperty(collectionName, out var collection) ||
                collection.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in collection.EnumerateArray())
            {
                if (!TryReadEvent(entry, definition, rules, out var snapshot))
                {
                    continue;
                }

                items.Add(snapshot);
            }
        }

        if (items.Count == 0)
        {
            throw new InvalidOperationException($"Timeline source '{definition.Url}' contained no valid events.");
        }

        return items
            .DistinctBy(item => item.ExternalId, StringComparer.Ordinal)
            .OrderBy(item => item.PublishedAtUtc)
            .ToArray();
    }

    private static bool TryReadEvent(
        JsonElement entry,
        SourceDefinition definition,
        TimelineRules rules,
        out SourceContentSnapshot snapshot)
    {
        snapshot = default!;
        var name = GetString(entry, "name");
        var startText = GetString(entry, "startDate");
        var endText = GetString(entry, "endDate");
        if (string.IsNullOrWhiteSpace(name) ||
            !TryParseServerTime(startText, rules, out var startAtUtc) ||
            !TryParseServerTime(endText, rules, out var endAtUtc) ||
            endAtUtc <= startAtUtc)
        {
            return false;
        }

        var trackerUrl = definition.Url;
        var officialUrl = TryGetOfficialUrl(entry) ?? trackerUrl;
        var description = GetString(entry, "description");
        var externalId = $"{Slug(name)}:{startAtUtc:yyyyMMddHHmm}";
        var details = new List<KeyValueItem>
        {
            new("Start", FormatDiscordTimestamp(startAtUtc)),
            new("End", FormatDiscordTimestamp(endAtUtc)),
        };
        var blocks = new List<ContentBlock>
        {
            new HeadingBlock(name, 1),
        };
        if (!string.IsNullOrWhiteSpace(description) && !string.Equals(description, "$undefined", StringComparison.Ordinal))
        {
            blocks.Add(new TextBlock(description, blocks.Count + 1));
        }

        if (TryGetCoverUrl(entry, trackerUrl) is { } coverUrl)
        {
            blocks.Add(new ImageBlock(
                coverUrl,
                $"{name} event artwork",
                blocks.Count + 1,
                "Artwork: WuWa Tracker"));
        }

        blocks.Add(new KeyValueBlock(details, blocks.Count + 1));
        blocks.Add(new LinkBlock("Schedule: WuWa Tracker", trackerUrl, blocks.Count + 1));
        snapshot = new SourceContentSnapshot(
            definition.Key,
            externalId,
            definition.Game,
            ContentKind.Event,
            name,
            officialUrl,
            ContentDocument.Create(blocks),
            startAtUtc,
            endAtUtc,
            PublishAtUtc: startAtUtc);
        return true;
    }

    private static bool TryParseServerTime(string? value, TimelineRules rules, out DateTimeOffset utc)
    {
        utc = default;
        if (!DateTime.TryParseExact(
                value,
                ["yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localTime))
        {
            return false;
        }

        utc = new DateTimeOffset(localTime, TimeSpan.FromHours(rules.ServerUtcOffsetHours)).ToUniversalTime();
        return true;
    }

    private static Uri? TryGetOfficialUrl(JsonElement entry)
    {
        var sourceUrl = GetString(entry, "sourceUrl");
        return Uri.TryCreate(sourceUrl, UriKind.Absolute, out var url) &&
               url.Scheme == Uri.UriSchemeHttps &&
               string.Equals(url.Host, "wutheringwaves.kurogames.com", StringComparison.OrdinalIgnoreCase)
            ? url
            : null;
    }

    private static Uri? TryGetCoverUrl(JsonElement entry, Uri trackerUrl)
    {
        var cover = GetString(entry, "coverImgSrc");
        if (string.IsNullOrWhiteSpace(cover) ||
            !Uri.TryCreate(trackerUrl, cover, out var coverUrl) ||
            coverUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(coverUrl.Host, trackerUrl.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return coverUrl;
    }

    private static string FormatDiscordTimestamp(DateTimeOffset value)
    {
        var unixTime = value.ToUnixTimeSeconds();
        return $"<t:{unixTime}:F> · <t:{unixTime}:R>";
    }

    private static string? GetString(JsonElement entry, string propertyName) =>
        entry.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Slug(string value)
    {
        var normalized = Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrEmpty(normalized) ? "event" : normalized;
    }

    [GeneratedRegex("self\\.__next_f\\.push\\((?<chunk>\\[1,\\\"(?:\\\\.|[^\\\"])*\\\"\\])\\)", RegexOptions.CultureInvariant)]
    private static partial Regex NextPayloadPattern();
}
