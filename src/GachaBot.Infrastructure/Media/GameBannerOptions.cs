using System.ComponentModel.DataAnnotations;

namespace GachaBot.Infrastructure.Media;

public sealed class GameBannerOptions
{
    public const string SectionName = "GameBanners";

    [Required]
    [Url]
    public string WutheringWavesSourceUrl { get; init; } = string.Empty;

    [Required]
    [Url]
    public string NevernessToEvernessSourceUrl { get; init; } = string.Empty;
}
