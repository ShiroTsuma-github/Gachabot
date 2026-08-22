using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Options;

namespace GachaBot.Infrastructure.Media;

public sealed record MediaObjectInfo(string ObjectKey, long Length, DateTimeOffset LastModifiedUtc);

public sealed record DownloadedMediaObject(string FullPath, long Length) : IAsyncDisposable
{
    public ValueTask DisposeAsync()
    {
        if (File.Exists(FullPath))
        {
            File.Delete(FullPath);
        }

        return ValueTask.CompletedTask;
    }
}

public interface IMediaObjectStore
{
    Task PutIfAbsentAsync(string objectKey, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken);

    Task<DownloadedMediaObject?> TryDownloadAsync(string objectKey, CancellationToken cancellationToken);

    IAsyncEnumerable<MediaObjectInfo> ListAsync(CancellationToken cancellationToken);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed class S3MediaObjectStore : IMediaObjectStore, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly S3MediaOptions _options;
    private readonly string _prefix;
    private readonly string _temporaryDirectory;

    public S3MediaObjectStore(
        IOptions<S3MediaOptions> options,
        IOptions<MediaArchiveOptions> archiveOptions)
    {
        _options = options.Value;
        _prefix = string.IsNullOrWhiteSpace(_options.Prefix)
            ? string.Empty
            : _options.Prefix.Trim('/') + "/";
        _temporaryDirectory = Path.GetFullPath(archiveOptions.Value.StagingPath);
        var config = new AmazonS3Config
        {
            ServiceURL = _options.Endpoint,
            AuthenticationRegion = _options.Region,
            ForcePathStyle = _options.ForcePathStyle,
        };
        _client = new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            config);
    }

    public async Task PutIfAbsentAsync(
        string objectKey,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        ValidateKey(objectKey);
        try
        {
            await _client.GetObjectMetadataAsync(_options.Bucket, objectKey, cancellationToken)
                .ConfigureAwait(false);
            return;
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // The object is new. A concurrent writer may upload it first; an identical overwrite is safe.
        }

        await using var input = new MemoryStream(content.ToArray(), writable: false);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket,
            Key = objectKey,
            InputStream = input,
            ContentType = contentType,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            UseChunkEncoding = false,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DownloadedMediaObject?> TryDownloadAsync(
        string objectKey,
        CancellationToken cancellationToken)
    {
        ValidateKey(objectKey);
        Directory.CreateDirectory(_temporaryDirectory);
        var path = Path.Combine(_temporaryDirectory, $"discord-{Guid.NewGuid():N}{Path.GetExtension(objectKey)}");
        try
        {
            using var response = await _client.GetObjectAsync(
                _options.Bucket,
                objectKey,
                cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await response.ResponseStream.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return new DownloadedMediaObject(path, output.Length);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            throw;
        }
    }

    public async IAsyncEnumerable<MediaObjectInfo> ListAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? continuationToken = null;
        do
        {
            var response = await _client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = _options.Bucket,
                Prefix = _prefix,
                ContinuationToken = continuationToken,
            }, cancellationToken).ConfigureAwait(false);
            foreach (var item in response.S3Objects)
            {
                yield return new MediaObjectInfo(
                    item.Key,
                    item.Size ?? 0,
                    (item.LastModified ?? DateTime.UnixEpoch).ToUniversalTime());
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }

    public Task DeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        ValidateKey(objectKey);
        return _client.DeleteObjectAsync(_options.Bucket, objectKey, cancellationToken);
    }

    public void Dispose() => _client.Dispose();

    private void ValidateKey(string objectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        if (!_options.Prefix.Equals(string.Empty, StringComparison.Ordinal) &&
            !objectKey.StartsWith(_prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Media object key is outside the configured prefix.");
        }
    }
}
