using System.ComponentModel.DataAnnotations;

namespace GachaBot.Infrastructure.Configuration;

public sealed class SourcePollingOptions
{
    public const string SectionName = "SourcePolling";

    [Range(1, 1_440)]
    public int IntervalMinutes { get; init; } = 15;
}
