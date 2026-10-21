using System.Net;
using Gannet.Core.Configuration;
using Gannet.Core.Services;
using Gannet.Providers.Search;
using Gannet.TestSupport;

namespace Gannet.Providers.Tests.Search;

public class HeadersFirstTests
{
    // A body with no declared length must be stopped by the counted read before HttpClient buffers all of it, and the response must still be disposed.
    [Fact]
    public async Task A_json_body_with_no_length_is_cut_off_at_the_cap_and_the_response_is_disposed()
    {
        var body = new CountingStream(64 * 1024 * 1024);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
        var serper = new SerperSearch(handler.Client(), "serper", new ServiceOptions { ApiKey = "k" });

        await Assert.ThrowsAsync<ServiceFailedException>(() => serper.SearchAsync("q", 3, CancellationToken.None));

        Assert.True(body.BytesRead <= (16 * 1024 * 1024) + (256 * 1024), $"read {body.BytesRead} bytes");
        Assert.True(body.Disposed);
    }

    [Fact]
    public async Task The_response_is_disposed_after_a_good_read()
    {
        var body = new CountingStream(0);
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body) });
        var serper = new SerperSearch(handler.Client(), "serper", new ServiceOptions { ApiKey = "k" });

        await Assert.ThrowsAsync<ServiceFailedException>(() => serper.SearchAsync("q", 3, CancellationToken.None));

        Assert.True(body.Disposed);
    }
}
