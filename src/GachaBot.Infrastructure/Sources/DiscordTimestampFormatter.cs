namespace GachaBot.Infrastructure.Sources;

internal static class DiscordTimestampFormatter
{
    internal static string Format(DateTimeOffset value) =>
        $"<t:{value.ToUnixTimeSeconds()}:F> \u00B7 <t:{value.ToUnixTimeSeconds()}:R>";
}
