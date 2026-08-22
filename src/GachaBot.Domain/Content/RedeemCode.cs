using GachaBot.Domain.Games;

namespace GachaBot.Domain.Content;

public sealed class RedeemCode
{
    private RedeemCode(GameKey game, string code, Uri sourceUrl, DateTimeOffset? expiresAtUtc)
    {
        Game = game;
        Code = code;
        SourceUrl = sourceUrl;
        ExpiresAtUtc = expiresAtUtc;
    }

    public GameKey Game { get; }

    public string Code { get; }

    public string Identity => $"{Game.ToSlug()}:{Code}";

    public Uri SourceUrl { get; }

    public DateTimeOffset? ExpiresAtUtc { get; }

    public static RedeemCode Create(
        GameKey game,
        string code,
        Uri sourceUrl,
        DateTimeOffset? expiresAtUtc = null)
    {
        var normalized = ContentBlockValidation.Required(code, nameof(code), 128).ToUpperInvariant();
        return new RedeemCode(
            game,
            normalized,
            ContentBlockValidation.PublicHttpUri(sourceUrl, nameof(sourceUrl)),
            expiresAtUtc);
    }

    public bool IsActiveAt(DateTimeOffset instant) => ExpiresAtUtc is null || instant <= ExpiresAtUtc.Value;
}
