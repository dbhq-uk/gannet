using System.Net;
using System.Net.Sockets;
using Gannet.Core;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Http;
using Gannet.Providers.Readers;
using Gannet.Providers.Search;
using Gannet.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gannet.Providers.Tests.Http;

public class BodyDeadlineTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

    /// <summary>A body that sends one byte of a JSON document and then stalls until it is cancelled.</summary>
    private sealed class StallingStream : Stream
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
            if (!sentFirst)
            {
                sentFirst = true;
                buffer.Span[0] = (byte)'{';
                return 1;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HttpResponseMessage Stalled() => new(HttpStatusCode.OK) { Content = new StreamContent(new StallingStream()) };

    [Fact]
    public async Task A_search_whose_body_stalls_fails_with_a_timeout_naming_the_service()
    {
        var stub = new StubHttpHandler(_ => Stalled());
        var search = new TavilySearch(stub.Client(), "tavily", new ServiceOptions { ApiKey = "k" }) { BodyDeadline = Short };

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => search.SearchAsync("q", 3, CancellationToken.None));

        Assert.Equal("Tavily timed out", error.Message);
    }

    [Fact]
    public async Task A_remote_reader_whose_body_stalls_fails_with_a_timeout_naming_the_service()
    {
        var stub = new StubHttpHandler(_ => Stalled());
        var reader = new JinaReader(stub.Client(), "jina", new ServiceOptions(), (_, _) => Task.FromResult(new[] { IPAddress.Parse("93.184.216.34") })) { BodyDeadline = Short };

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => reader.ReadAsync(new Uri("https://site.example/a"), 500, CancellationToken.None));

        Assert.Equal("Jina Reader timed out", error.Message);
    }

    [Fact]
    public async Task A_text_body_that_stalls_fails_with_a_timeout()
    {
        using var response = Stalled();

        var error = await Assert.ThrowsAsync<ServiceFailedException>(() => JsonRead.ReadTextAsync(response, "Bright Data", 1000, CancellationToken.None, Short));

        Assert.Equal("Bright Data timed out", error.Message);
    }

    [Fact]
    public async Task The_callers_cancellation_during_a_body_read_is_not_mapped()
    {
        using var cts = new CancellationTokenSource(Short);
        using var response = Stalled();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => JsonRead.ParseAsync(response, "Tavily", cts.Token, TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public void The_default_body_deadline_is_thirty_seconds() => Assert.Equal(TimeSpan.FromSeconds(30), JsonRead.DefaultBodyDeadline);

    private static async Task<(TcpListener Listener, Task Server)> LoopbackServer()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    using var client = await listener.AcceptTcpClientAsync();
                    var stream = client.GetStream();
                    await stream.ReadAtLeastAsync(new byte[4096], 1, throwOnEndOfStream: false);
                    await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray());
                }
            }
            catch (Exception e) when (e is ObjectDisposedException or SocketException or InvalidOperationException or IOException)
            {
            }
        });
        await Task.Yield();
        return (listener, server);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public async Task A_blank_proxy_url_keeps_the_loopback_guard_on(string? proxyUrl)
    {
        var (listener, _) = await LoopbackServer();
        try
        {
            var settings = new Dictionary<string, string?> { ["Gannet:Readers:Proxy:Url"] = proxyUrl };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGannetCore(configuration);
            services.AddGannetProviders(configuration);
            using var provider = services.BuildServiceProvider();
            using var invoker = new HttpMessageInvoker(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(HttpClientNames.Reader));
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/");

            await Assert.ThrowsAnyAsync<Exception>(() => invoker.SendAsync(request, CancellationToken.None));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task With_private_addresses_allowed_the_loopback_connect_works()
    {
        var (listener, _) = await LoopbackServer();
        try
        {
            var settings = new Dictionary<string, string?> { ["Gannet:Readers:AllowPrivateAddresses"] = "true" };
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddGannetCore(configuration);
            services.AddGannetProviders(configuration);
            using var provider = services.BuildServiceProvider();
            using var invoker = new HttpMessageInvoker(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(HttpClientNames.Reader));
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/");

            using var response = await invoker.SendAsync(request, CancellationToken.None);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            listener.Stop();
        }
    }
}
