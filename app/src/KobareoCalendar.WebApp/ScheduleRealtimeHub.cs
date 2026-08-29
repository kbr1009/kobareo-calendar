using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;

namespace KobareoCalendar.WebApp;

public sealed class ScheduleRealtimeHub
{
    private sealed record Connection(WebSocket Socket, SemaphoreSlim SendLock);
    private readonly ConcurrentDictionary<Guid, Connection> connections = new();

    public async Task ListenAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        connections[id] = new Connection(socket, new SemaphoreSlim(1, 1));
        var buffer = new byte[256];
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            if (connections.TryRemove(id, out var connection)) connection.SendLock.Dispose();
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
        }
    }

    public async Task BroadcastScheduleChangedAsync(Guid scheduleId, string action, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { type = "schedule.changed", scheduleId, action });
        foreach (var pair in connections.ToArray())
        {
            var connection = pair.Value;
            if (connection.Socket.State != WebSocketState.Open) { connections.TryRemove(pair.Key, out _); continue; }
            await connection.SendLock.WaitAsync(cancellationToken);
            try { await connection.Socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken); }
            catch (WebSocketException) { connections.TryRemove(pair.Key, out _); }
            finally { connection.SendLock.Release(); }
        }
    }
}
