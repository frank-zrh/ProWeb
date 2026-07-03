using System.Net.WebSockets;
using ProWeb.Server.Middleware;
using ProWeb.Shared.Protocol;
using ProWeb.Shared.Serialization;

namespace ProWeb.Server.Endpoints;

/// <summary>
/// WebSocket endpoint /v1/stream. Authentication is enforced by the session middleware before the
/// upgrade. Each binary frame is an AES-256-GCM sealed <see cref="StreamFrame"/>; this first
/// implementation echoes frames back (with a basic forwarding skeleton) using the session key.
/// </summary>
public static class StreamEndpoint
{
    public static void MapStreamEndpoint(this WebApplication app)
    {
        app.Map("/v1/stream", HandleStreamAsync);
    }

    private static async Task HandleStreamAsync(HttpContext context, EnvelopeCodec codec)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var session = context.GetSessionContext();
        if (session is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        var buffer = new byte[64 * 1024];

        while (socket.State == WebSocketState.Open)
        {
            var received = await ReceiveFullAsync(socket, buffer, context.RequestAborted).ConfigureAwait(false);
            if (received is null)
                break;

            if (received.Value.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None)
                    .ConfigureAwait(false);
                break;
            }

            StreamFrame frame;
            try
            {
                var aad = System.Text.Encoding.UTF8.GetBytes(session.SessionId);
                frame = codec.Decode<StreamFrame>(received.Value.Data, session.SessionKey, aad);
            }
            catch (Exception)
            {
                await socket.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "bad frame", CancellationToken.None)
                    .ConfigureAwait(false);
                break;
            }

            // Echo / basic forwarding skeleton: reflect the frame back to the client.
            var reply = new StreamFrame
            {
                RequestId = frame.RequestId,
                Seq = frame.Seq,
                Fin = frame.Fin,
                Payload = frame.Payload,
            };
            var aadOut = System.Text.Encoding.UTF8.GetBytes(session.SessionId);
            var sealedReply = codec.Encode(reply, session.SessionKey, aadOut);
            await socket.SendAsync(sealedReply, WebSocketMessageType.Binary, true, context.RequestAborted)
                .ConfigureAwait(false);
        }
    }

    private const int MaxMessageBytes = 8 * 1024 * 1024; // 8 MB cap to bound memory per message.

    private static async Task<(WebSocketMessageType MessageType, byte[] Data)?> ReceiveFullAsync(
        WebSocket socket, byte[] buffer, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            try
            {
                result = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (WebSocketException)
            {
                return null;
            }

            if (result.MessageType == WebSocketMessageType.Close)
                return (WebSocketMessageType.Close, Array.Empty<byte>());

            if (ms.Length + result.Count > MaxMessageBytes)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.MessageTooBig, "message exceeds size limit", CancellationToken.None)
                    .ConfigureAwait(false);
                return null;
            }

            ms.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return (result.MessageType, ms.ToArray());
    }
}
