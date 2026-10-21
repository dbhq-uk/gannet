using System.Net.WebSockets;

namespace Gannet.Web;

/// <summary>
/// Endpoints that prove the hosting path: a health check, and a WebSocket echo used to check that
/// WebSocket upgrades pass through Cloudflare Access to the container. The echo goes in Task 44.
/// </summary>
internal static class HostingCheckEndpoints
{
    public static void MapHostingCheck(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Text("{\"status\":\"ok\"}", "application/json"));

        app.Map("/ws-echo", async (HttpContext context) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open)
            {
                var result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", context.RequestAborted);
                    break;
                }

                await socket.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, context.RequestAborted);
            }
        });
    }
}
