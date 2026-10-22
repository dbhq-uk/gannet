using System.Net;
using System.Text;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Core.Storage;
using Gannet.Providers.Storage;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Storage;

public class S3BlobStoreTests
{
    private const string SecretKey = "s3-secret-access-key-value";
    private const string KeyId = "AKIDEXAMPLE0123";
    private const string Xmlns = "http://s3.amazonaws.com/doc/2006-03-01/";

    private static S3StorageOptions Options() => new()
    {
        ServiceUrl = "https://r2.example.test",
        Bucket = "gannet-bucket",
        AccessKeyId = KeyId,
        SecretAccessKey = SecretKey,
    };

    private static IBlobStore Store(StubHttpHandler stub, TimeSpan? bodyDeadline = null) =>
        S3BlobStoreFactory.Create(Options(), stub, bodyDeadline);

    private static HttpResponseMessage Xml(string xml, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };

    private static HttpResponseMessage Error(HttpStatusCode status, string code, string message = "The request failed.") =>
        Xml($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>{code}</Code><Message>{message}</Message><RequestId>REQ1</RequestId></Error>", status);

    private static string ListPage(bool truncated, string? token, params string[] keys) =>
        $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><ListBucketResult xmlns=\"{Xmlns}\"><Name>gannet-bucket</Name><Prefix>runs/</Prefix>"
        + $"<KeyCount>{keys.Length}</KeyCount><MaxKeys>1000</MaxKeys><IsTruncated>{(truncated ? "true" : "false")}</IsTruncated>"
        + (token is null ? string.Empty : $"<NextContinuationToken>{token}</NextContinuationToken>")
        + string.Concat(keys.Select(k => $"<Contents><Key>{k}</Key><LastModified>2026-10-21T10:00:00.000Z</LastModified><ETag>&quot;abc&quot;</ETag><Size>2</Size><StorageClass>STANDARD</StorageClass></Contents>"))
        + "</ListBucketResult>";

    [EnvironmentFact("GANNET_TEST_S3_URL", "GANNET_TEST_S3_BUCKET", "GANNET_TEST_S3_KEY", "GANNET_TEST_S3_SECRET")]
    public async Task Put_get_list_and_delete_against_a_real_s3_endpoint()
    {
        var store = S3BlobStoreFactory.Create(new S3StorageOptions
        {
            ServiceUrl = Environment.GetEnvironmentVariable("GANNET_TEST_S3_URL"),
            Bucket = Environment.GetEnvironmentVariable("GANNET_TEST_S3_BUCKET"),
            AccessKeyId = Environment.GetEnvironmentVariable("GANNET_TEST_S3_KEY"),
            SecretAccessKey = Environment.GetEnvironmentVariable("GANNET_TEST_S3_SECRET"),
        });
        var prefix = "test/" + Guid.NewGuid().ToString("N") + "/";

        await store.PutAsync(prefix + "a.json", Encoding.UTF8.GetBytes("{\"a\":1}"), CancellationToken.None);
        await store.PutAsync(prefix + "b.json", Encoding.UTF8.GetBytes("{}"), CancellationToken.None);

        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString((await store.GetAsync(prefix + "a.json", CancellationToken.None))!));
        Assert.Equal([prefix + "a.json", prefix + "b.json"], await store.ListAsync(prefix, CancellationToken.None));
        Assert.Null(await store.GetAsync(prefix + "missing.json", CancellationToken.None));

        await store.DeleteAsync(prefix + "a.json", CancellationToken.None);
        await store.DeleteAsync(prefix + "b.json", CancellationToken.None);
        Assert.Empty(await store.ListAsync(prefix, CancellationToken.None));
    }

    [Fact]
    public void Missing_settings_fail_with_the_setting_name()
    {
        var error = Assert.Throws<InvalidOperationException>(() => S3BlobStoreFactory.Create(new S3StorageOptions { ServiceUrl = "https://x" }));
        Assert.Contains("Gannet:Storage:S3:Bucket", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ServiceUrl")]
    [InlineData("Bucket")]
    [InlineData("AccessKeyId")]
    [InlineData("SecretAccessKey")]
    public void Each_missing_setting_is_named_and_no_value_is_printed(string missing)
    {
        var options = Options();
        typeof(S3StorageOptions).GetProperty(missing)!.SetValue(options, " ");

        var error = Assert.Throws<InvalidOperationException>(() => S3BlobStoreFactory.Create(options));

        Assert.Contains("Gannet:Storage:S3:" + missing, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyId, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_service_url_that_is_not_http_or_https_is_refused_without_printing_it()
    {
        var options = Options();
        options.ServiceUrl = "ftp://user:pass@r2.example.test";

        var error = Assert.Throws<InvalidOperationException>(() => S3BlobStoreFactory.Create(options));

        Assert.Contains("Gannet:Storage:S3:ServiceUrl", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("pass", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("UPPER.json")]
    [InlineData("a/../b")]
    [InlineData("runs/a.json\n")]
    [InlineData("runs/a.tmp-1")]
    [InlineData("con")]
    [InlineData("runs/a.")]
    [InlineData("")]
    public async Task An_unsafe_key_is_refused_before_any_request(string key)
    {
        var stub = new StubHttpHandler(_ => Xml("<x/>"));
        var store = Store(stub);

        await Assert.ThrowsAsync<ArgumentException>(() => store.PutAsync(key, new byte[] { 1 }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync(key, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => store.DeleteAsync(key, CancellationToken.None));
        if (key.Length > 0)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.ListAsync(key, CancellationToken.None));
        }

        Assert.Empty(stub.Requests);
    }

    [Fact]
    public async Task Put_sends_the_bytes_to_a_path_style_url_and_leaves_the_content_alone()
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var content = Encoding.UTF8.GetBytes("{\"a\":1}");
        var copy = (byte[])content.Clone();

        await Store(stub).PutAsync("runs/a.json", content, CancellationToken.None);

        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("https://r2.example.test/gannet-bucket/runs/a.json", request.Uri.GetLeftPart(UriPartial.Path));
        Assert.Equal("{\"a\":1}", request.Body);
        Assert.Equal("application/json", request.Headers["Content-Type"]);
        Assert.Equal(copy, content);
    }

    [Fact]
    public async Task Put_of_an_empty_value_and_a_non_json_key_works()
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Store(stub).PutAsync("runs/a.bin", ReadOnlyMemory<byte>.Empty, CancellationToken.None);

        var request = Assert.Single(stub.Requests);
        Assert.Equal("application/octet-stream", request.Headers["Content-Type"]);
        Assert.Equal(string.Empty, request.Body);
    }

    [Fact]
    public async Task Requests_are_signed_for_the_configured_region_and_never_carry_the_secret()
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Store(stub).PutAsync("runs/a.json", new byte[] { 1 }, CancellationToken.None);

        var request = Assert.Single(stub.Requests);
        var authorization = request.Headers["Authorization"];
        Assert.Contains("Credential=" + KeyId + "/", authorization, StringComparison.Ordinal);
        Assert.Contains("/auto/s3/aws4_request", authorization, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, string.Join('\n', request.Headers.Values) + request.Uri + request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_returns_the_bytes()
    {
        var stub = new StubHttpHandler(_ => StubHttpHandler.Json("{\"a\":1}"));

        var bytes = await Store(stub).GetAsync("runs/a.json", CancellationToken.None);

        Assert.Equal("{\"a\":1}", Encoding.UTF8.GetString(bytes!));
        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https://r2.example.test/gannet-bucket/runs/a.json", request.Uri.GetLeftPart(UriPartial.Path));
    }

    [Fact]
    public async Task Get_of_a_missing_key_is_null()
    {
        var stub = new StubHttpHandler(_ => Error(HttpStatusCode.NotFound, "NoSuchKey", "The specified key does not exist."));

        Assert.Null(await Store(stub).GetAsync("runs/missing.json", CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_bucket_is_a_failure_not_a_missing_key()
    {
        var stub = new StubHttpHandler(_ => Error(HttpStatusCode.NotFound, "NoSuchBucket", "The specified bucket does not exist."));

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Store(stub).GetAsync("runs/a.json", CancellationToken.None));

        Assert.Contains("Gannet:Storage:S3:Bucket", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_follows_the_continuation_token_and_sorts_ordinally()
    {
        var stub = new StubHttpHandler(request => request.RequestUri!.Query.Contains("continuation-token", StringComparison.Ordinal)
            ? Xml(ListPage(false, null, "runs/a.json", "runs/c.json"))
            : Xml(ListPage(true, "tok1", "runs/d.json", "runs/b.json")));

        var keys = await Store(stub).ListAsync("runs/", CancellationToken.None);

        Assert.Equal(["runs/a.json", "runs/b.json", "runs/c.json", "runs/d.json"], keys);
        Assert.Equal(2, stub.Requests.Count);
        Assert.All(stub.Requests, r => Assert.Equal("https://r2.example.test/gannet-bucket", r.Uri.GetLeftPart(UriPartial.Path).TrimEnd('/')));
        Assert.Contains("list-type=2", stub.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Contains("prefix=runs%2F", stub.Requests[0].Uri.Query, StringComparison.Ordinal);
        Assert.Contains("continuation-token=tok1", stub.Requests[1].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_with_no_matches_is_empty_and_an_empty_prefix_lists_everything()
    {
        var stub = new StubHttpHandler(_ => Xml(ListPage(false, null)));

        Assert.Empty(await Store(stub).ListAsync(string.Empty, CancellationToken.None));
        Assert.DoesNotContain("prefix=runs", stub.Requests[0].Uri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_listing_that_stays_truncated_with_the_same_token_stops_with_a_failure()
    {
        var stub = new StubHttpHandler(_ => Xml(ListPage(true, "same", "runs/a.json")));

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Store(stub).ListAsync("runs/", CancellationToken.None));

        Assert.Contains("listing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(stub.Requests.Count <= 3);
    }

    [Fact]
    public async Task Delete_sends_a_delete_and_a_missing_key_is_not_an_error()
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Store(stub).DeleteAsync("runs/gone.json", CancellationToken.None);

        var request = Assert.Single(stub.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("https://r2.example.test/gannet-bucket/runs/gone.json", request.Uri.GetLeftPart(UriPartial.Path));

        var notFound = new StubHttpHandler(_ => Error(HttpStatusCode.NotFound, "NoSuchKey"));
        await Store(notFound).DeleteAsync("runs/gone.json", CancellationToken.None);
    }

    [Fact]
    public async Task A_403_names_the_setting_and_never_echoes_the_key_the_server_returned()
    {
        var stub = new StubHttpHandler(_ => Error(HttpStatusCode.Forbidden, "InvalidAccessKeyId", $"The access key {KeyId} secret {SecretKey} is wrong."));

        var error = await Assert.ThrowsAsync<AuthRequiredException>(() => Store(stub).GetAsync("runs/a.json", CancellationToken.None));

        Assert.Contains("Gannet:Storage:S3:AccessKeyId", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyId, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidAccessKeyId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_403_on_every_operation_is_an_auth_error()
    {
        var stub = new StubHttpHandler(_ => Error(HttpStatusCode.Forbidden, "AccessDenied"));
        var store = Store(stub);

        await Assert.ThrowsAsync<AuthRequiredException>(() => store.PutAsync("runs/a.json", new byte[] { 1 }, CancellationToken.None));
        await Assert.ThrowsAsync<AuthRequiredException>(() => store.ListAsync("runs/", CancellationToken.None));
        await Assert.ThrowsAsync<AuthRequiredException>(() => store.DeleteAsync("runs/a.json", CancellationToken.None));
    }

    [Fact]
    public async Task A_429_is_rate_limited_and_a_500_is_a_failure_with_fixed_text()
    {
        var limited = new StubHttpHandler(_ => Error(HttpStatusCode.TooManyRequests, "SlowDown", SecretKey));
        await Assert.ThrowsAsync<RateLimitedException>(() => Store(limited).GetAsync("runs/a.json", CancellationToken.None));

        var broken = new StubHttpHandler(_ => Error(HttpStatusCode.InternalServerError, "InternalError", SecretKey));
        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Store(broken).GetAsync("runs/a.json", CancellationToken.None));
        Assert.Contains("500", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretKey, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("InternalError", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_network_failure_is_a_failure_with_fixed_text()
    {
        var stub = new StubHttpHandler(_ => throw new HttpRequestException($"connect to r2.example.test failed for {KeyId}"));

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Store(stub).GetAsync("runs/a.json", CancellationToken.None));

        Assert.DoesNotContain(KeyId, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("r2.example.test", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_turned_into_a_failure()
    {
        var stub = new StubHttpHandler(_ => StubHttpHandler.Json("{}"));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(stub).GetAsync("runs/a.json", cancelled.Token));
    }

    [Fact]
    public async Task An_object_that_declares_more_than_16_MB_is_refused()
    {
        var stub = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream([1, 2, 3])) };
            response.Content.Headers.ContentLength = 16L * 1024 * 1024 + 1;
            return response;
        });

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Store(stub).GetAsync("runs/big.json", CancellationToken.None));

        Assert.Contains("too large", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_object_with_no_declared_length_is_cut_off_at_16_MB()
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new EndlessContent() });

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => Store(stub).GetAsync("runs/big.json", CancellationToken.None));

        Assert.Contains("too large", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_that_stalls_after_the_headers_times_out_with_a_typed_error()
    {
        var stub = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StallingContent() });

        var error = await Assert.ThrowsAsync<ServiceFailedException>(
            () => Store(stub, TimeSpan.FromMilliseconds(300)).GetAsync("runs/a.json", CancellationToken.None));

        Assert.Contains("timed out", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_store_is_an_S3BlobStore_that_can_be_disposed()
    {
        var store = Store(new StubHttpHandler(_ => Xml("<x/>")));

        Assert.IsType<S3BlobStore>(store);
        ((IDisposable)store).Dispose();
    }

    /// <summary>A body with no length that never ends.</summary>
    private sealed class EndlessContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new ZeroStream(stall: false));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>Sends one byte, then waits until it is cancelled.</summary>
    private sealed class StallingContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new NotSupportedException();

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new ZeroStream(stall: true));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    private sealed class ZeroStream(bool stall) : Stream
    {
        private bool sentFirst;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (stall && sentFirst)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            sentFirst = true;
            buffer.Span.Clear();
            return stall ? 1 : buffer.Length;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
