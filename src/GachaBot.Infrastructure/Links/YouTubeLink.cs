namespace GachaBot.Infrastructure.Links;

internal static class YouTubeLink
{
    private const string CanonicalPrefix = "https://www.youtube.com/watch?v=";

    internal static bool TryCanonicalize(Uri candidate, out Uri canonical)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        canonical = null!;
        if (!candidate.IsAbsoluteUri ||
            (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var host = candidate.IdnHost.ToLowerInvariant();
        string? videoId = host switch
        {
            "youtu.be" or "www.youtu.be" => FirstPathSegment(candidate),
            "youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com" =>
                VideoIdFromYouTubeUri(candidate),
            "youtube-nocookie.com" or "www.youtube-nocookie.com" => VideoIdFromPath(candidate),
            _ => null,
        };
        if (!IsVideoId(videoId))
        {
            return false;
        }

        canonical = new Uri(CanonicalPrefix + videoId);
        return true;
    }

    private static string? VideoIdFromYouTubeUri(Uri candidate) =>
        candidate.AbsolutePath.Equals("/watch", StringComparison.OrdinalIgnoreCase)
            ? QueryValue(candidate.Query, "v")
            : VideoIdFromPath(candidate);

    private static string? VideoIdFromPath(Uri candidate)
    {
        var segments = candidate.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
        {
            return null;
        }

        return segments[0].Equals("embed", StringComparison.OrdinalIgnoreCase) ||
            segments[0].Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
            segments[0].Equals("live", StringComparison.OrdinalIgnoreCase)
                ? Uri.UnescapeDataString(segments[1])
                : null;
    }

    private static string? FirstPathSegment(Uri candidate)
    {
        var segment = candidate.AbsolutePath.Trim('/').Split('/', 2)[0];
        return segment.Length == 0 ? null : Uri.UnescapeDataString(segment);
    }

    private static string? QueryValue(string query, string key)
    {
        foreach (var component in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = component.Split('=', 2);
            if (pair.Length == 2 && Uri.UnescapeDataString(pair[0]).Equals(key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair[1]);
            }
        }

        return null;
    }

    private static bool IsVideoId(string? value) => value is { Length: 11 } &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
