using Microsoft.AspNetCore.Mvc;

namespace SiagroB1.Web.Market;

/// <summary>
/// Erros dos endpoints REST do Mercado: só a mensagem pt-BR em text/plain. BadRequest(string) dentro de um
/// ODataController vira objeto de erro OData, e num ControllerBase pode sair como string JSON conforme o Accept.
/// Os CRUD OData continuam com o formato de erro OData (o modelo OData do UI5 espera esse formato).
/// </summary>
public static class MarketTextResults
{
    public static ContentResult Text(int statusCode, string message) => new()
    {
        StatusCode = statusCode, ContentType = "text/plain; charset=utf-8", Content = message,
    };

    public static ContentResult BadRequest(string message) => Text(StatusCodes.Status400BadRequest, message);

    public static ContentResult Forbidden(string message) => Text(StatusCodes.Status403Forbidden, message);

    public static ContentResult NotFound(string message) => Text(StatusCodes.Status404NotFound, message);
}
