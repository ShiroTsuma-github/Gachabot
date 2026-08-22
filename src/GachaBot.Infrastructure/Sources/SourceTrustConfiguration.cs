using GachaBot.Application.Ingestion;
using Microsoft.Extensions.Configuration;

namespace GachaBot.Infrastructure.Sources;

internal static class SourceTrustConfiguration
{
    internal static SourceTrust Resolve(
        IConfiguration? configuration,
        string sourceKey,
        SourceTrust defaultTrust)
    {
        if (configuration is null)
        {
            return defaultTrust;
        }

        var section = configuration.GetSection($"Sources:{sourceKey}");
        return section.GetValue("Enabled", true)
            ? section.GetValue("Trust", defaultTrust)
            : SourceTrust.Disabled;
    }
}
