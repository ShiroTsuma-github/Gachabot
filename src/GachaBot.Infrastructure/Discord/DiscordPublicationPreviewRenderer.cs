using GachaBot.Application.Publishing;
using GachaBot.Domain.Content;

namespace GachaBot.Infrastructure.Discord;

public sealed class DiscordPublicationPreviewRenderer : IPublicationPreviewRenderer
{
    public PublicationPreview Render(string title, ContentDocument document, Uri? sourceUrl)
    {
        var composition = DiscordMessageComposer.Compose(title, document, sourceUrl);
        return new PublicationPreview(
            composition.Text,
            composition.Messages.Select(message => new PublicationPreviewMessage(
                message.Content,
                message.Embeds.Select(embed => new PublicationPreviewEmbed(
                    embed.Title,
                    embed.Description,
                    embed.Url,
                    embed.Color,
                    embed.Fields.Select(field => new PublicationPreviewField(
                        field.Name,
                        field.Value,
                        field.Inline)).ToArray(),
                    embed.Image is null
                        ? null
                        : new PublicationPreviewImage(
                            embed.Image.Url,
                            embed.Image.AltText,
                            embed.Image.Caption),
                    embed.Footer,
                    embed.CharacterCount)).ToArray()))
                .ToArray());
    }
}
