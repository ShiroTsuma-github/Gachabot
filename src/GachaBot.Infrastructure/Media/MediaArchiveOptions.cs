using System.ComponentModel.DataAnnotations;

namespace GachaBot.Infrastructure.Media;

public sealed class MediaArchiveOptions
{
    public const string SectionName = "MediaArchive";

    [Required]
    public string RootPath { get; init; } = "data/media";

    [Required]
    public string StagingPath { get; init; } = "data/media-staging";

    [Range(1, 100)]
    public int MaximumDownloadSizeMegabytes { get; init; } = 50;

    [Range(1, 9)]
    public int MaximumStoredImageSizeMegabytes { get; init; } = 9;

    [Range(320, 8192)]
    public int MaximumImageDimension { get; init; } = 4096;

    [Range(10, 300)]
    public int AttemptTimeoutSeconds { get; init; } = 60;

    [Range(30, 900)]
    public int TotalRequestTimeoutSeconds { get; init; } = 180;
}
