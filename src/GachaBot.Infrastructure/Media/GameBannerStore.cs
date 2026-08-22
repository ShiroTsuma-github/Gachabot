using GachaBot.Domain.Games;
using Microsoft.Extensions.Options;

namespace GachaBot.Infrastructure.Media;

public sealed record GameBannerAttachment(string GameName, string FullPath, string FileName, long Length);

public sealed class GameBannerStore(IMediaObjectStore objectStore)
{
    public async Task<GameBannerAttachment?> TryDownloadAsync(GameKey game, CancellationToken cancellationToken)
    {
        var downloaded = await objectStore.TryDownloadAsync(ObjectKey(game), cancellationToken).ConfigureAwait(false);
        return downloaded is null
            ? null
            : new GameBannerAttachment(GameName(game), downloaded.FullPath, FileName(game), downloaded.Length);
    }

    internal static string ObjectKey(GameKey game) => game switch
    {
        GameKey.WutheringWaves => "media/static/wuthering-waves-redeem-banner.jpg",
        GameKey.NevernessToEverness => "media/static/neverness-to-everness-redeem-banner.jpg",
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, "Unsupported game banner."),
    };

    internal static string GameName(GameKey game) => game switch
    {
        GameKey.WutheringWaves => "Wuthering Waves",
        GameKey.NevernessToEverness => "Neverness to Everness",
        _ => game.ToString(),
    };

    private static string FileName(GameKey game) => game switch
    {
        GameKey.WutheringWaves => "wuthering-waves-redeem-codes.jpg",
        GameKey.NevernessToEverness => "neverness-to-everness-redeem-codes.jpg",
        _ => "redeem-codes.jpg",
    };
}

public sealed class GameBannerSeeder(
    IHttpClientFactory httpClientFactory,
    IMediaObjectStore objectStore,
    IOptions<GameBannerOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await SeedAsync(GameKey.WutheringWaves, options.Value.WutheringWavesSourceUrl, cancellationToken)
            .ConfigureAwait(false);
        await SeedAsync(GameKey.NevernessToEverness, options.Value.NevernessToEvernessSourceUrl, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SeedAsync(GameKey game, string sourceUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        request.Headers.UserAgent.ParseAdd("GachaBot/1.0 (+private game banner cache)");
        using var response = await httpClientFactory.CreateClient("GameBanners")
            .SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength > 10L * 1_024L * 1_024L)
        {
            throw new InvalidOperationException($"The {GameBannerStore.GameName(game)} banner exceeds 10 MiB.");
        }

        await objectStore.PutIfAbsentAsync(
            GameBannerStore.ObjectKey(game),
            bytes,
            response.Content.Headers.ContentType?.MediaType ?? "image/jpeg",
            cancellationToken).ConfigureAwait(false);
    }
}
