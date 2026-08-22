using GachaBot.Application.Ingestion;

namespace GachaBot.Infrastructure.Discord;

public sealed class SourceMediaPublicationPolicy(
    IReadOnlyDictionary<string, SourceTrust> sourceTrust)
{
    public bool UsesRemoteImages(string sourceKey) =>
        sourceTrust.TryGetValue(sourceKey, out var trust) && trust == SourceTrust.Official;
}
