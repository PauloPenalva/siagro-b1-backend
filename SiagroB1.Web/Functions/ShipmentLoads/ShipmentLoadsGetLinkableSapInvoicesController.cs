using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ShipmentLoads;

/// <summary>
/// Invoices do SAP que podem ser vinculadas à carga (diálogo "Vincular NF do SAP", GAC-1226).
/// O serviço só é registrado em modo SAPB1; fora dele a função responde 400.
/// </summary>
public class ShipmentLoadsGetLinkableSapInvoicesController(
    IServiceProvider services)
    : ODataController
{
    /// <summary>
    /// Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.
    /// </summary>
    [EnableQuery]
    [HttpGet("odata/ShipmentLoadsGetLinkableSapInvoices(Key={key},DateFrom={dateFrom},DateTo={dateTo})")]
    public async Task<ActionResult<IEnumerable<SapLinkableInvoiceDto>>> Get(
        [FromRoute] Guid key,
        [FromRoute] string? dateFrom,
        [FromRoute] string? dateTo,
        CancellationToken ct)
    {
        // Resolvido sob demanda: só existe no ramo SAPB1 do Program.
        var service = services.GetService<SapLinkableInvoicesService>();
        if (service == null)
            return BadRequest("Disponível somente com integração SAP Business One.");

        // Parâmetro string do OData é anulável e chega com as aspas: parse estrito, sem "hoje"
        // como fallback (mesmo cuidado de PurchaseDischargesList).
        if (!TryParse(dateFrom, out var from) || !TryParse(dateTo, out var to))
            return BadRequest("Informe o período no formato aaaa-mm-dd.");

        try
        {
            return Ok(await service.ListAsync(key, from, to, ct));
        }
        catch (Exception e) when (e is BusinessException or DefaultException or ApplicationException)
        {
            return BadRequest(e.Message);
        }
    }

    private static bool TryParse(string? raw, out DateTime value) =>
        DateTime.TryParseExact(raw?.Trim('\''), "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out value);
}
