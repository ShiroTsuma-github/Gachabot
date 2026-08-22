using System.ComponentModel.DataAnnotations;

namespace GachaBot.Infrastructure.Media;

public sealed class S3MediaOptions
{
    public const string SectionName = "S3Media";

    [Required]
    [Url]
    public string Endpoint { get; init; } = string.Empty;

    [Required]
    public string Region { get; init; } = "garage";

    [Required]
    public string AccessKey { get; init; } = string.Empty;

    [Required]
    public string SecretKey { get; init; } = string.Empty;

    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$")]
    public string Bucket { get; init; } = string.Empty;

    [RegularExpression("^[a-zA-Z0-9!_.*'()/-]*$")]
    public string Prefix { get; init; } = "media";

    public bool ForcePathStyle { get; init; } = true;

    [Range(1, 168)]
    public int GarbageCollectionGraceHours { get; init; } = 24;
}
