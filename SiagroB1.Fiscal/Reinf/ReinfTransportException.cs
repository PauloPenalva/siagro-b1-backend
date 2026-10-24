namespace SiagroB1.Fiscal.Reinf;

/// <summary>Falha de transporte com a API da EFD-Reinf (StatusCode 0 = sem resposta HTTP).</summary>
public sealed class ReinfTransportException(int statusCode, string body)
    : Exception($"Falha na comunicação com a EFD-Reinf (HTTP {statusCode}).")
{
    public int StatusCode { get; } = statusCode;
    public string Body { get; } = body;
}
