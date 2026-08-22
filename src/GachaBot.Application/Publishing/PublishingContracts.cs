using GachaBot.Domain.Content;
using GachaBot.Domain.Games;

namespace GachaBot.Application.Publishing;

public sealed record PublicationPayload(
    Guid ContentId,
    string SourceKey,
    string? ExternalId,
    GameKey Game,
    ContentKind Kind,
    GuildDestination Destination,
    string Title,
    Uri? SourceUrl,
    ContentDocument Document,
    string? ExistingProviderMessageId = null,
    PublicationPurpose Purpose = PublicationPurpose.Standard);

public enum PublicationPurpose
{
    Standard = 1,
    EventStart = 2,
    EventEndingReminder = 3,
}

public interface IEventPublicationScheduleStore
{
    Task ReconcileForGuildAsync(ulong guildId, CancellationToken cancellationToken);
}

public sealed record PublicationPreview(
    string Text,
    IReadOnlyList<PublicationPreviewMessage> Messages);

public sealed record PublicationPreviewMessage(
    string? Content,
    IReadOnlyList<PublicationPreviewEmbed> Embeds);

public sealed record PublicationPreviewEmbed(
    string? Title,
    string? Description,
    Uri? Url,
    int Color,
    IReadOnlyList<PublicationPreviewField> Fields,
    PublicationPreviewImage? Image,
    string? Footer,
    int CharacterCount);

public sealed record PublicationPreviewField(string Name, string Value, bool Inline);

public sealed record PublicationPreviewImage(Uri Url, string AltText, string? Caption);

public sealed record PublishReceipt(string ProviderMessageId, DateTimeOffset PublishedAtUtc);

public interface IDiscordPublisher
{
    Task<PublishReceipt> PublishAsync(
        PublicationPayload payload,
        CancellationToken cancellationToken);

    Task DeleteMessagesAsync(
        GuildDestination destination,
        string providerMessageIds,
        CancellationToken cancellationToken);
}

public interface IPublicationPreviewRenderer
{
    PublicationPreview Render(string title, ContentDocument document, Uri? sourceUrl);
}
