using System.Text.Json;

namespace SiagroB1.Web.Sockets;

public class WsMessage
{
    public string Action { get; set; }
    public string RequestId { get; set; }
    public JsonElement Data { get; set; }
}