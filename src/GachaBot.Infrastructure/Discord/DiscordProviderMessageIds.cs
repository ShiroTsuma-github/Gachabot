using System.Globalization;

namespace GachaBot.Infrastructure.Discord;

public static class DiscordProviderMessageIds
{
    public static IReadOnlyList<ulong> Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("Stored Discord message id is invalid.");
        }

        var parts = value.Split(',', StringSplitOptions.None);
        var ids = new ulong[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!ulong.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out ids[index]) ||
                ids[index] == 0)
            {
                throw new InvalidOperationException("Stored Discord message id is invalid.");
            }
        }

        return ids;
    }

    public static string Format(IEnumerable<ulong> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var ids = values.ToArray();
        if (ids.Length == 0)
        {
            throw new InvalidOperationException("At least one Discord message id is required.");
        }

        return string.Join(',', ids.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }
}
