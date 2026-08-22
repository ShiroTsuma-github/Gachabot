using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GachaBot.Infrastructure.Media;

public sealed record ArchivedMediaAttachment(
    string FullPath,
    string FileName,
    string ContentType,
    long Length);

public sealed class MediaArchiveCatalog(IOptions<MediaArchiveOptions> options) : IDisposable
{
    private const string ManifestName = "manifest.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private readonly SemaphoreSlim _manifestLock = new(1, 1);

    public string RootPath => Path.GetFullPath(options.Value.RootPath);

    public string GetItemDirectory(string sourceKey, string externalId)
    {
        var sourceSegment = SafeSegment(sourceKey, 128);
        if (!string.Equals(sourceSegment, sourceKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Source key '{sourceKey}' is not safe as a media directory.");
        }

        var directory = Path.GetFullPath(Path.Combine(RootPath, sourceSegment, SafeSegment(externalId, 96)));
        EnsureWithinRoot(directory);
        return directory;
    }

    public async Task SaveAsync(
        string sourceKey,
        string externalId,
        Uri sourceUrl,
        Application.Media.ArchivedMedia media,
        CancellationToken cancellationToken)
    {
        var directory = GetItemDirectory(sourceKey, externalId);
        var manifestPath = Path.Combine(directory, ManifestName);
        await _manifestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false)
                ?? new MediaManifest(sourceKey, externalId, []);
            var entry = new MediaManifestEntry(
                sourceUrl.AbsoluteUri,
                media.RelativePath,
                media.ContentType,
                media.Length,
                media.Sha256,
                media.State,
                media.ProcessingNote);
            var entries = manifest.Assets
                .Where(asset => !string.Equals(asset.SourceUrl, entry.SourceUrl, StringComparison.Ordinal))
                .Append(entry)
                .OrderBy(asset => asset.SourceUrl, StringComparer.Ordinal)
                .ToArray();
            var updated = manifest with { Assets = entries };
            var temporaryPath = manifestPath + $".{Guid.NewGuid():N}.tmp";
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16_384,
                FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(output, updated, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(temporaryPath, manifestPath, overwrite: true);
        }
        finally
        {
            _manifestLock.Release();
        }
    }

    public async Task<ArchivedMediaAttachment?> TryResolveAsync(
        string sourceKey,
        string externalId,
        Uri sourceUrl,
        CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(GetItemDirectory(sourceKey, externalId), ManifestName);
        var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var entry = manifest?.Assets.SingleOrDefault(asset =>
            string.Equals(asset.SourceUrl, sourceUrl.AbsoluteUri, StringComparison.Ordinal));
        if (entry is null)
        {
            return null;
        }

        var fullPath = Path.GetFullPath(Path.Combine(RootPath, entry.RelativePath));
        EnsureWithinRoot(fullPath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var extension = Path.GetExtension(fullPath);
        var urlHash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl.AbsoluteUri)))[..8];
        return new ArchivedMediaAttachment(
            fullPath,
            $"media-{entry.Sha256[..12]}-{urlHash}{extension}",
            entry.ContentType,
            new FileInfo(fullPath).Length);
    }

    public void Dispose()
    {
        _manifestLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private static async Task<MediaManifest?> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16_384,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<MediaManifest>(input, JsonOptions, cancellationToken)
            .ConfigureAwait(false);
    }

    private void EnsureWithinRoot(string path)
    {
        if (!path.StartsWith(RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved media path escapes the archive root.");
        }
    }

    private static string SafeSegment(string value, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = new string(value
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-')
            .ToArray()).Trim('-');
        if (normalized.Length > 0 && normalized.Length <= maximumLength &&
            string.Equals(normalized, value, StringComparison.Ordinal))
        {
            return normalized;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..10];
        var prefixLength = Math.Max(1, maximumLength - hash.Length - 1);
        var prefix = normalized.Length == 0 ? "item" : normalized[..Math.Min(normalized.Length, prefixLength)];
        return $"{prefix}-{hash}";
    }

    private sealed record MediaManifest(
        string SourceKey,
        string ExternalId,
        IReadOnlyList<MediaManifestEntry> Assets);

    private sealed record MediaManifestEntry(
        string SourceUrl,
        string RelativePath,
        string ContentType,
        long Length,
        string Sha256,
        Application.Media.MediaArchiveState State,
        string? ProcessingNote);
}
