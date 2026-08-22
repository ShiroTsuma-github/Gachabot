using GachaBot.Application.Ingestion;

namespace GachaBot.Infrastructure.Sources;

public interface IContentRetentionPolicy
{
    bool ShouldArchive(
        string sourceKey,
        DateTimeOffset? sourcePublishedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset nowUtc);
}

public sealed class OfficialContentRetentionPolicy(IEnumerable<SourceDefinition> definitions)
    : IContentRetentionPolicy
{
    private readonly HashSet<string> _officialSourceKeys = definitions
        .Where(definition => definition.Trust == SourceTrust.Official)
        .Select(definition => definition.Key)
        .ToHashSet(StringComparer.Ordinal);

    public bool ShouldArchive(
        string sourceKey,
        DateTimeOffset? sourcePublishedAtUtc,
        DateTimeOffset createdAtUtc,
        DateTimeOffset nowUtc)
    {
        if (!_officialSourceKeys.Contains(sourceKey))
        {
            return false;
        }

        var referenceTime = sourcePublishedAtUtc ?? createdAtUtc;
        return referenceTime <= nowUtc.AddMonths(-1);
    }
}
