using System.ComponentModel.DataAnnotations;

namespace GachaBot.Infrastructure.Discord;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    [Required]
    public string BotToken { get; init; } = string.Empty;

    public ulong GuildId { get; init; }

    public ulong ChannelId { get; init; }

    public string ActivityName { get; init; } = "Schmidley";

    public ulong[] AdministratorUserIds { get; init; } = [];
}
