namespace GachaBot.Infrastructure.Configuration;

public sealed class DatabaseStorageOptions
{
    public const string SectionName = "DatabaseStorage";

    [System.ComponentModel.DataAnnotations.Required]
    public string ConnectionString { get; init; } = string.Empty;
}
