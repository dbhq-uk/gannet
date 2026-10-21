using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Gannet.Web.Tests;

public class HostingCheckEndpointsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Healthz_returns_ok()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"status\":\"ok\"}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ws_echo_returns_the_frame_it_was_sent()
    {
        var wsClient = factory.Server.CreateWebSocketClient();
        using var socket = await wsClient.ConnectAsync(new Uri(factory.Server.BaseAddress, "/ws-echo"), CancellationToken.None);
        var payload = Encoding.UTF8.GetBytes("hello gannet");
        await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        var buffer = new byte[256];
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
        Assert.Equal("hello gannet", Encoding.UTF8.GetString(buffer, 0, result.Count));
    }
}
