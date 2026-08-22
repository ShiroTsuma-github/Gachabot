using Microsoft.Extensions.Logging;

namespace GachaBot.Infrastructure.Discord;

public sealed partial class DiscordChannelPublisher
{
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Information,
        Message = "Prepared Discord publication {ContentId}: {MessageCount} messages, {AttachmentCount} archived image attachments and {RemoteImageCount} remote image URLs.")]
    private static partial void LogPublicationPrepared(
        ILogger logger,
        Guid contentId,
        int messageCount,
        int attachmentCount,
        int remoteImageCount);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Information,
        Message = "Sending Discord message {MessageIndex}/{MessageCount} for {ContentId}: {AttachmentCount} attachments, {AttachmentBytes} bytes.")]
    private static partial void LogMessageSendStarted(
        ILogger logger,
        Guid contentId,
        int messageIndex,
        int messageCount,
        int attachmentCount,
        long attachmentBytes);
}
