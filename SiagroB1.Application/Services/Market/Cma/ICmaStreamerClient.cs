using System.Text.Json;

namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>
/// Cliente do streamer da CMA (porta do StreamerConnection.java do cma-data-feed). Sem estado por
/// empresa: credenciais e sessão vão em cada chamada, para uma instância servir várias empresas.
/// </summary>
public interface ICmaStreamerClient
{
    /// <summary>LoginRequest; devolve o sessionId. Falha de autenticação lança <see cref="CmaAuthenticationException"/>.</summary>
    Task<string> LoginAsync(CmaCredentials credentials, CancellationToken cancellationToken);

    /// <summary>
    /// CreatePersistentChannelRequest: lê o canal e entrega cada QuotesUpdateResponse. Termina quando o servidor
    /// fecha o canal; ErrorResponse no canal lança <see cref="CmaProtocolException"/> (sessão expirada/derrubada).
    /// </summary>
    IAsyncEnumerable<JsonElement> OpenChannelAsync(CmaCredentials credentials, string sessionId, CancellationToken cancellationToken);

    /// <summary>QuotesRequest em tempo real; devolve o retrato inicial (arrQuotes) do QuotesResponse.</summary>
    Task<IReadOnlyList<JsonElement>> SubscribeAsync(CmaCredentials credentials, string sessionId,
        IReadOnlyCollection<(int? SourceId, string Symbol)> symbols, CancellationToken cancellationToken);

    /// <summary>PingRequest (heartbeat).</summary>
    Task PingAsync(CmaCredentials credentials, string sessionId, CancellationToken cancellationToken);
}

/// <summary>Resposta da CMA fora do esperado. A mensagem nunca carrega URL nem credenciais.</summary>
public class CmaProtocolException(string message) : Exception(message);

/// <summary>Login recusado pela CMA.</summary>
public sealed class CmaAuthenticationException() : CmaProtocolException("Falha de autenticação na CMA");
