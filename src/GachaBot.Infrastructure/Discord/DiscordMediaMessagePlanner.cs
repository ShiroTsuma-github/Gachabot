using GachaBot.Application.Publishing;
using GachaBot.Infrastructure.Media;

namespace GachaBot.Infrastructure.Discord;

public sealed record DiscordOutboundAttachment(
    string FullPath,
    string FileName,
    string Description,
    long Length,
    bool DeleteAfterUse = false);

public sealed record DiscordPreparedMessage(
    string? Content,
    IReadOnlyList<DiscordRichEmbed> Embeds,
    IReadOnlyList<DiscordOutboundAttachment> Attachments);

public sealed class DiscordMediaMessagePlanner
{
    public const long MaximumFileBytes = 10L * 1_024L * 1_024L;
    private readonly MediaAssetRegistry? _mediaAssets;
    private readonly IMediaObjectStore? _objectStore;
    private readonly MediaArchiveCatalog? _legacyCatalog;
    private readonly GameBannerStore? _gameBanners;
    private readonly SourceMediaPublicationPolicy _publicationPolicy;

    public DiscordMediaMessagePlanner(
        MediaAssetRegistry mediaAssets,
        IMediaObjectStore objectStore,
        GameBannerStore gameBanners,
        SourceMediaPublicationPolicy publicationPolicy)
    {
        _mediaAssets = mediaAssets;
        _objectStore = objectStore;
        _gameBanners = gameBanners;
        _publicationPolicy = publicationPolicy;
    }

    // Retained solely for reading archives created before S3Media was introduced.
    public DiscordMediaMessagePlanner(
        MediaArchiveCatalog legacyCatalog,
        SourceMediaPublicationPolicy publicationPolicy)
    {
        _legacyCatalog = legacyCatalog;
        _publicationPolicy = publicationPolicy;
    }

    public async Task<IReadOnlyList<DiscordPreparedMessage>> PrepareAsync(
        PublicationPayload payload,
        DiscordComposition composition,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(composition);
        var prepared = new List<DiscordPreparedMessage>();
        var banner = payload.Kind == GachaBot.Domain.Content.ContentKind.RedeemCode && _gameBanners is not null
            ? await _gameBanners.TryDownloadAsync(payload.Game, cancellationToken).ConfigureAwait(false)
            : null;
        var bannerUsed = false;
        foreach (var outbound in composition.Messages)
        {
            var content = outbound.Content;
            var embeds = new List<DiscordRichEmbed>();
            var attachments = new List<DiscordOutboundAttachment>();
            foreach (var embed in outbound.Embeds)
            {
                var useBanner = banner is not null && !bannerUsed;
                var attachment = useBanner
                    ? new ArchivedMediaAttachment(
                        banner!.FullPath,
                        banner.FileName,
                        "image/jpeg",
                        banner.Length)
                    : await ResolveAsync(payload, embed.Image, cancellationToken).ConfigureAwait(false);
                if (attachment is not null && attachments.Count > 0)
                {
                    AddPrepared(prepared, content, embeds, attachments);
                    content = null;
                    embeds = [];
                    attachments = [];
                }

                var effectiveImage = useBanner
                    ? new DiscordImage(
                        new Uri("https://attachment.invalid/redeem-banner"),
                        $"{banner!.GameName} redeem codes banner",
                        $"{banner.GameName} · Redeem codes")
                    : embed.Image;
                var effectiveEmbed = attachment is null || effectiveImage is null
                    ? embed
                    : embed with
                    {
                        Title = useBanner ? PrefixRedeemTitle(embed.Title, banner!.GameName) : embed.Title,
                        Image = effectiveImage with { AttachmentFileName = attachment.FileName },
                    };
                embeds.Add(effectiveEmbed);
                if (attachment is not null)
                {
                    attachments.Add(new DiscordOutboundAttachment(
                        attachment.FullPath,
                        attachment.FileName,
                        Truncate(effectiveImage!.AltText, 1_024),
                        attachment.Length,
                        DeleteAfterUse: true));
                bannerUsed |= useBanner;
                }
            }

            AddPrepared(prepared, content, embeds, attachments);
        }

        return prepared;
    }

    private async Task<ArchivedMediaAttachment?> ResolveAsync(
        PublicationPayload payload,
        DiscordImage? image,
        CancellationToken cancellationToken)
    {
        if (image is null ||
            string.IsNullOrWhiteSpace(payload.ExternalId) ||
            (_publicationPolicy.UsesRemoteImages(payload.SourceKey) &&
             payload.Kind != GachaBot.Domain.Content.ContentKind.Event))
        {
            return null;
        }

        if (_legacyCatalog is not null)
        {
            var legacy = await _legacyCatalog.TryResolveAsync(
                payload.SourceKey, payload.ExternalId, image.Url, cancellationToken).ConfigureAwait(false);
            return legacy?.Length <= MaximumFileBytes ? legacy : null;
        }

        var stored = await _mediaAssets!.TryGetAsync(
            payload.SourceKey,
            payload.ExternalId,
            image.Url,
            cancellationToken).ConfigureAwait(false);
        if (stored is null || stored.StoredLength > MaximumFileBytes)
        {
            return null;
        }

        var downloaded = await _objectStore!.TryDownloadAsync(stored.ObjectKey, cancellationToken)
            .ConfigureAwait(false);
        if (downloaded is null)
        {
            return null;
        }

        if (downloaded.Length > MaximumFileBytes)
        {
            await downloaded.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        var urlHash = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(image.Url.AbsoluteUri)))[..8];
        var extension = Path.GetExtension(stored.ObjectKey);
        return new ArchivedMediaAttachment(
            downloaded.FullPath,
            $"media-{stored.Sha256[..12]}-{urlHash}{extension}",
            stored.ContentType,
            downloaded.Length);
    }

    private static void AddPrepared(
        List<DiscordPreparedMessage> target,
        string? content,
        List<DiscordRichEmbed> embeds,
        List<DiscordOutboundAttachment> attachments)
    {
        if (content is null && embeds.Count == 0)
        {
            return;
        }

        target.Add(new DiscordPreparedMessage(content, embeds.ToArray(), attachments.ToArray()));
    }

    private static string PrefixRedeemTitle(string? title, string gameName)
    {
        var prefix = $"{gameName} · Redeem Codes";
        return string.IsNullOrWhiteSpace(title)
            ? prefix
            : $"{prefix} — {title}";
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
