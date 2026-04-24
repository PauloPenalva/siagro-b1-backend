using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using SiagroB1.Client.Interfaces;

namespace SiagroB1.Client.Readers;

public class TcpScaleReader(IConfiguration config) : IScaleReader
{
    private decimal _lastWeight;
    
    public decimal GetWeight() => _lastWeight;

    public async Task StartAsync(CancellationToken ct)
    {
        var ip = config["ScaleTcp:Ip"];
        var port = int.Parse(config["ScaleTcp:Port"]);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(ip, port, ct);

                var stream = client.GetStream();
                var buffer = new byte[1024];
                var sb = new StringBuilder();

                while (!ct.IsCancellationRequested && client.Connected)
                {
                    var read = await stream.ReadAsync(buffer, ct);
                    if (read == 0) break;

                    sb.Append(Encoding.ASCII.GetString(buffer, 0, read));

                    while (sb.ToString().Contains("\n"))
                    {
                        var line = ExtractLine(sb);

                        var weight = Parse(line);
                        if (weight.HasValue)
                            _lastWeight = weight.Value;
                    }
                }
            }
            catch
            {
                await Task.Delay(3000, ct);
            }
        }
    }

    private string ExtractLine(StringBuilder sb)
    {
        var str = sb.ToString();
        var idx = str.IndexOf('\n');

        var line = str.Substring(0, idx).Trim();
        sb.Remove(0, idx + 1);

        return line;
    }

    private decimal? Parse(string input)
    {
        var match = Regex.Match(input, @"(\d+)");
        return match.Success ? decimal.Parse(match.Value) : null;
    }
}