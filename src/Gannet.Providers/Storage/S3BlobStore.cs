using System.Net;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Core.Storage;
using Gannet.Providers.Http;

namespace Gannet.Providers.Storage;

internal static class S3BlobStoreFactory
{
    /// <param name="options">The configured S3 settings. A missing one fails here, naming the setting and never its value.</param>
    /// <param name="handler">Only tests pass one: the HTTP layer the AWS client sends through.</param>
    /// <param name="bodyDeadline">Only tests pass one: how long reading an object body may take.</param>
    public static IBlobStore Create(S3StorageOptions options, HttpMessageHandler? handler = null, TimeSpan? bodyDeadline = null)
    {
        static string Require(string? value, string setting) =>
            string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"S3 storage needs {setting}.") : value;

        var serviceUrl = Require(options.ServiceUrl, "Gannet:Storage:S3:ServiceUrl");
        if (JsonRead.WebUri(serviceUrl) is null)
        {
            throw new InvalidOperationException("S3 storage needs Gannet:Storage:S3:ServiceUrl to be an http or https address with no user name.");
        }

        var bucket = Require(options.Bucket, "Gannet:Storage:S3:Bucket");
        var credentials = new BasicAWSCredentials(
            Require(options.AccessKeyId, "Gannet:Storage:S3:AccessKeyId"),
            Require(options.SecretAccessKey, "Gannet:Storage:S3:SecretAccessKey"));

        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region) ? "auto" : options.Region,

            // R2 rejects the newer default checksum headers on some operations; send them only when required.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            MaxErrorRetry = 2,
            Timeout = TimeSpan.FromSeconds(60),
        };
        if (handler is not null)
        {
            config.HttpClientFactory = new HandlerHttpClientFactory(handler);
        }

        return new S3BlobStore(new AmazonS3Client(credentials, config), bucket, bodyDeadline ?? JsonRead.DefaultBodyDeadline);
    }

    private sealed class HandlerHttpClientFactory(HttpMessageHandler handler) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig) => new(handler, disposeHandler: false);

        public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;

        public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => false;
    }
}

/// <summary>
/// Any S3-compatible store: Cloudflare R2, MinIO, or S3 itself. The AWS SDK's exceptions can carry request details, so none of their text is
/// passed on: every error is mapped to one of gannet's own with fixed words.
/// </summary>
internal sealed class S3BlobStore(IAmazonS3 s3, string bucket, TimeSpan bodyDeadline) : IBlobStore, IDisposable
{
    private const string Service = "S3 storage";

    // A listing that keeps saying "more" is a broken server, not a big bucket: at 1000 keys a page this is 100 million keys.
    private const int MaxListPages = 100_000;

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, CancellationToken ct)
    {
        BlobKeys.Validate(key);
        var bytes = System.Runtime.InteropServices.MemoryMarshal.TryGetArray(content, out var segment) && segment.Array is not null
            ? segment
            : new ArraySegment<byte>(content.ToArray());
        using var stream = new MemoryStream(bytes.Array!, bytes.Offset, bytes.Count, writable: false);
        await Call(() => s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = stream,
            ContentType = key.EndsWith(".json", StringComparison.Ordinal) ? "application/json" : "application/octet-stream",
            UseChunkEncoding = false,
        }, ct), ct);
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        BlobKeys.Validate(key);
        var response = await CallAllowingMissing(() => s3.GetObjectAsync(bucket, key, ct), ct);
        if (response is null)
        {
            return null;
        }

        using (response)
        {
            return await ReadBodyAsync(response, ct);
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
    {
        BlobKeys.ValidatePrefix(prefix);
        var keys = new List<string>();
        var seenTokens = new HashSet<string>(StringComparer.Ordinal);
        var request = new ListObjectsV2Request { BucketName = bucket, Prefix = prefix.Length == 0 ? null : prefix };
        for (var page = 0; page < MaxListPages; page++)
        {
            var response = await Call(() => s3.ListObjectsV2Async(request, ct), ct);
            keys.AddRange((response.S3Objects ?? []).Select(o => o.Key));
            if (response.IsTruncated != true)
            {
                return [.. keys.Order(StringComparer.Ordinal)];
            }

            var token = response.NextContinuationToken;
            if (string.IsNullOrEmpty(token) || !seenTokens.Add(token))
            {
                break;
            }

            request.ContinuationToken = token;
        }

        throw new ServiceFailedException(Service, $"{Service} returned a listing that does not end.");
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        BlobKeys.Validate(key);
        await CallAllowingMissing(() => s3.DeleteObjectAsync(bucket, key, ct), ct);
    }

    public void Dispose() => s3.Dispose();

    /// <summary>Runs one SDK call. Every failure, a 404 included, becomes a typed gannet error with fixed text.</summary>
    private async Task<T> Call<T>(Func<Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call();
        }
        catch (Exception e) when (IsTransport(e, ct))
        {
            throw Map(e);
        }
    }

    /// <summary>Like <see cref="Call{T}"/>, but a missing key (404 other than a missing bucket) gives null. Only Get and Delete may treat a 404 so.</summary>
    private async Task<T?> CallAllowingMissing<T>(Func<Task<T>> call, CancellationToken ct)
        where T : class
    {
        try
        {
            return await call();
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound && e.ErrorCode != "NoSuchBucket")
        {
            return null;
        }
        catch (Exception e) when (IsTransport(e, ct))
        {
            throw Map(e);
        }
    }

    /// <summary>
    /// A failure of the SDK or the network, including its own timeouts (which surface as cancellation). The caller's own cancellation is not one:
    /// it passes through untouched.
    /// </summary>
    private static bool IsTransport(Exception e, CancellationToken ct) =>
        !ct.IsCancellationRequested
        && e is AmazonServiceException or AmazonClientException or HttpRequestException or IOException or OperationCanceledException;

    private async Task Call(Func<Task> call, CancellationToken ct) =>
        await Call(async () =>
        {
            await call();
            return true;
        }, ct);

    private ServiceException Map(Exception e)
    {
        if (e is OperationCanceledException)
        {
            return new ServiceFailedException(Service, $"{Service} timed out.");
        }

        if (e is not AmazonS3Exception s3Error)
        {
            return new ServiceFailedException(Service, $"{Service} could not be reached.");
        }

        var status = (int)s3Error.StatusCode;
        return s3Error.StatusCode switch
        {
            HttpStatusCode.NotFound when s3Error.ErrorCode == "NoSuchBucket" => new ServiceFailedException(Service, $"{Service} says the bucket does not exist. Check Gannet:Storage:S3:Bucket."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new AuthRequiredException(Service, $"{Service} refused the request ({status}). Check Gannet:Storage:S3:AccessKeyId and Gannet:Storage:S3:SecretAccessKey."),
            HttpStatusCode.TooManyRequests => new RateLimitedException(Service, null, $"{Service} is limiting the request rate (429)."),
            _ => new ServiceFailedException(Service, $"{Service} answered {status} ({s3Error.StatusCode})."),
        };
    }

    /// <summary>The same 16 MB cap as JSON responses, counted as the bytes arrive, under a deadline for the whole body.</summary>
    private async Task<byte[]> ReadBodyAsync(GetObjectResponse response, CancellationToken ct)
    {
        var tooLarge = new ServiceFailedException(Service, $"{Service} returned an object that is too large.");
        if (response.ContentLength > JsonRead.MaxBodyBytes)
        {
            throw tooLarge;
        }

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(bodyDeadline);
        using var buffer = new MemoryStream();
        try
        {
            var chunk = new byte[81920];
            int read;
            while ((read = await response.ResponseStream.ReadAsync(chunk, limit.Token).AsTask().WaitAsync(limit.Token)) > 0)
            {
                if (buffer.Length + read > JsonRead.MaxBodyBytes)
                {
                    throw tooLarge;
                }

                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new ServiceFailedException(Service, $"{Service} timed out while sending an object.");
        }
        catch (Exception e) when (IsTransport(e, ct))
        {
            throw Map(e);
        }
    }
}
