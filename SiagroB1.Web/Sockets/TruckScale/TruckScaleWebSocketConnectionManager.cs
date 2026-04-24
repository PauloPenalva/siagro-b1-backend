using System.Net.WebSockets;

namespace SiagroB1.Web.Sockets.TruckScale;

public class TruckScaleWebSocketConnectionManager
{
    private readonly Dictionary<string, WebSocket> _connections = new();

    public void Add(string truckScaleId, WebSocket socket)
        => _connections[truckScaleId] = socket;

    public WebSocket? Get(string truckScaleId)
        => _connections.TryGetValue(truckScaleId, out var ws) ? ws : null;

    public void Remove(string truckScaleId)
        => _connections.Remove(truckScaleId);
}