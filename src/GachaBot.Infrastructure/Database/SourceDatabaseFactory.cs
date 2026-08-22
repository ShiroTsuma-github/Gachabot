using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace GachaBot.Infrastructure.Database;

public interface ISourceDatabaseFactory
{
    IReadOnlyList<string> DatabaseKeys { get; }

    AppDbContext CreateDbContext(string sourceKey);
}

public sealed class SourceDatabaseFactory(string connectionString, IEnumerable<string> sourceKeys)
    : ISourceDatabaseFactory
{
    public const string ManualDatabaseKey = "manual";

    private readonly string _connectionString = string.IsNullOrWhiteSpace(connectionString)
        ? throw new ArgumentException("PostgreSQL connection string is required.", nameof(connectionString))
        : connectionString;

    public IReadOnlyList<string> DatabaseKeys { get; } = sourceKeys
        .Append(ManualDatabaseKey)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(key => key, StringComparer.Ordinal)
        .ToArray();

    public AppDbContext CreateDbContext(string sourceKey)
    {
        ValidateKey(sourceKey);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .ReplaceService<IModelCacheKeyFactory, AppDbContextModelCacheKeyFactory>()
            .Options;
        return new AppDbContext(options, sourceKey);
    }

    private static void ValidateKey(string sourceKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        if (sourceKey.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidOperationException($"Source key '{sourceKey}' is not safe as a PostgreSQL schema name.");
        }
    }
}
