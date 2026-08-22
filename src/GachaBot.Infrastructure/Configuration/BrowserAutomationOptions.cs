using System.ComponentModel.DataAnnotations;

namespace GachaBot.Infrastructure.Configuration;

public sealed class BrowserAutomationOptions
{
    public const string SectionName = "BrowserAutomation";

    public bool Headless { get; set; } = true;

    [Required]
    public string Locale { get; set; } = "pl-PL";

    [Required]
    public string TimezoneId { get; set; } = "Europe/Warsaw";

    [Required]
    public string ProfilePath { get; set; } = "data/browser-profile";
}
