using System.Data.Common;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>
/// Classificação das falhas da conexão com a CMA para log e status. As credenciais vão na query string da
/// URL, que só é montada dentro do <see cref="CmaStreamerClient"/>: falha de transporte (HTTP, socket, TLS,
/// tempo esgotado) pode carregar a URL na Message, então dela só saem metadados; o resto (banco, cifra,
/// JSON...) nunca viu a URL e pode ir inteiro para o log.
/// </summary>
public static class CmaFailure
{
    public const string DatabaseMessage = "Banco da empresa indisponível ou desatualizado";

    public static bool IsTransport(Exception e) =>
        Chain(e).Any(x => x is HttpRequestException or SocketException or IOException or UriFormatException
            or AuthenticationException or TimeoutException)
        || e is OperationCanceledException;

    public static bool IsDatabase(Exception e) =>
        Chain(e).Any(x => x is DbException or DbUpdateException);

    /// <summary>Metadados seguros de uma falha de transporte: erro HTTP, status, erro de socket e tipos da cadeia.</summary>
    public static string TransportDetails(Exception e)
    {
        var chain = Chain(e).ToList();
        var http = chain.OfType<HttpRequestException>().FirstOrDefault();
        var socket = chain.OfType<SocketException>().FirstOrDefault();
        return $"HttpRequestError={http?.HttpRequestError.ToString() ?? "-"}, " +
               $"Status={(http?.StatusCode is { } status ? ((int)status).ToString() : "-")}, " +
               $"Socket={socket?.SocketErrorCode.ToString() ?? "-"}, " +
               $"Tipos={string.Join(" > ", chain.Select(x => x.GetType().Name))}";
    }

    private static IEnumerable<Exception> Chain(Exception e)
    {
        for (Exception? current = e; current is not null; current = current.InnerException)
            yield return current;
    }
}
