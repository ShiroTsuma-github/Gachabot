using Discord;

namespace GachaBot.Infrastructure.Discord;

internal static class DiscordMessageSender
{
    internal static Task<IUserMessage> SendAsync(
        ITextChannel channel,
        DiscordPreparedMessage message,
        CancellationToken cancellationToken)
    {
        if (message.Attachments.Count == 0)
        {
            return channel.SendMessageAsync(
                message.Content,
                allowedMentions: AllowedMentions.None,
                embeds: BuildEmbeds(message.Embeds),
                options: new RequestOptions { CancelToken = cancellationToken });
        }

        var streams = message.Attachments
            .Select(attachment => File.OpenRead(attachment.FullPath))
            .ToArray();
        var files = message.Attachments
            .Select((attachment, index) => new FileAttachment(
                streams[index],
                attachment.FileName,
                attachment.Description))
            .ToArray();
        return SendFilesAndDisposeAsync(channel, message, files, streams, cancellationToken);
    }

    internal static Embed[] BuildEmbeds(IReadOnlyList<DiscordRichEmbed> embeds) =>
        embeds.Select(BuildEmbed).ToArray();

    private static async Task<IUserMessage> SendFilesAndDisposeAsync(
        ITextChannel channel,
        DiscordPreparedMessage message,
        FileAttachment[] files,
        Stream[] streams,
        CancellationToken cancellationToken)
    {
        try
        {
            return await channel.SendFilesAsync(
                files,
                message.Content,
                allowedMentions: AllowedMentions.None,
                embeds: BuildEmbeds(message.Embeds),
                options: new RequestOptions { CancelToken = cancellationToken }).ConfigureAwait(false);
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static Embed BuildEmbed(DiscordRichEmbed embed)
    {
        var builder = new EmbedBuilder().WithColor(new Color((uint)embed.Color));
        if (embed.Title is not null)
        {
            builder.WithTitle(embed.Title);
        }

        if (embed.Description is not null)
        {
            builder.WithDescription(embed.Description);
        }

        if (embed.Url is not null)
        {
            builder.WithUrl(embed.Url.AbsoluteUri);
        }

        if (embed.Image is not null)
        {
            builder.WithImageUrl(embed.Image.AttachmentFileName is null
                ? embed.Image.Url.AbsoluteUri
                : $"attachment://{embed.Image.AttachmentFileName}");
        }

        if (embed.Footer is not null)
        {
            builder.WithFooter(embed.Footer);
        }

        foreach (var field in embed.Fields)
        {
            builder.AddField(field.Name, field.Value, field.Inline);
        }

        return builder.Build();
    }
}
