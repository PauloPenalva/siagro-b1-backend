using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.SalesInvoices;

/// <summary>
/// Retorna um documento de saída LEGADO: devolve os romaneios escolhidos e, conforme o destino,
/// deixa a mercadoria pronta para refaturamento ou a descarrega num armazém.
/// </summary>
/// <remarks>
/// A escolha é por ROMANEIO; <c>Quantities</c> é OPCIONAL e, quando vem, é um array PARALELO a
/// <c>StorageTransactionKeys</c> — omitido, cada romaneio volta inteiro, que é o contrato
/// anterior e o único que o destino "segue viagem" aceita. Contagens diferentes são recusadas
/// aqui, antes do serviço: é erro de montagem do payload, não de negócio, e sem esta checagem
/// viraria um pareamento silenciosamente errado entre romaneio e quantidade.
/// <para>
/// ⚠️ Declarado no EDM como <c>Collection(Edm.Double)</c>, e não <c>Decimal</c>: o UI5 serializa
/// <c>Edm.Decimal</c> como string e o backend recusa com um 400 que não nomeia o campo.
/// </para>
/// </remarks>
public class SalesInvoicesReturnController(
    SalesInvoicesReturnService service
    ) : ODataController
{
    [HttpPost("odata/SalesInvoicesReturn")]
    public async Task<IActionResult> PostAsync(ODataActionParameters parameters)
    {
        try
        {
            // ⚠️ ODataActionParameters chega NULO quando falta um parâmetro declarado no EDM:
            // sem esta guarda, o TryGetValue seguinte estoura NRE e vira 500 de corpo vazio.
            if (parameters == null)
            {
                return BadRequest("Missing required parameters");
            }

            if (!parameters.TryGetValue("Key", out var keyObj) || keyObj == null)
            {
                return BadRequest("Missing required parameters");
            }

            if (!parameters.TryGetValue("StorageTransactionKeys", out var keysObj) ||
                keysObj is not IEnumerable<Guid> shipmentKeys)
            {
                return BadRequest("Selecione ao menos um romaneio a devolver.");
            }

            var destination = ParseDestination(Text(parameters, "Destination"));

            if (destination == null)
            {
                return BadRequest("Informe o destino da mercadoria devolvida.");
            }

            var keyList = shipmentKeys.ToList();
            var quantities = Quantities(parameters, "Quantities");

            if (quantities != null && quantities.Count != keyList.Count)
            {
                return BadRequest(
                    "A lista de romaneios e a de quantidades têm tamanhos diferentes.");
            }

            var request = new SalesInvoiceReturnRequest(
                SalesInvoiceKey: Guid.Parse(keyObj.ToString()!),
                Shipments: keyList
                    .Select((shipmentKey, index) => new SalesInvoiceReturnShipment(
                        shipmentKey, quantities?[index]))
                    .ToList(),
                Destination: destination.Value,
                DestinationWarehouseCode: Text(parameters, "DestinationWarehouseCode"),
                Reason: Text(parameters, "Reason") ?? string.Empty);

            var userName = User.Identity?.Name ?? "Unknown";

            var returnInvoice = await service.ExecuteAsync(request, userName);

            return Ok(new
            {
                returnInvoice.Key,
                returnInvoice.InvoiceNumber,
            });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound(e.Message);
            }

            return BadRequest(e.Message);
        }
    }

    private static RefusalDestination? ParseDestination(string? value) => value switch
    {
        "Rebilling" => RefusalDestination.Rebilling,
        "Warehouse" => RefusalDestination.Warehouse,
        _ => null,
    };

    /// <summary>
    /// As quantidades informadas, ou <c>null</c> quando o parâmetro não veio — que é o pedido de
    /// devolver cada romaneio inteiro. Uma lista VAZIA também vira <c>null</c>: o UI5 manda
    /// <c>[]</c> quando não há nada a parear, e tratá-la como lista mandaria o serviço comparar
    /// contagens que nunca vão bater.
    /// </summary>
    private static List<decimal>? Quantities(ODataActionParameters parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value is not IEnumerable sequence)
            return null;

        var result = new List<decimal>();

        foreach (var item in sequence)
        {
            result.Add(item switch
            {
                double d => (decimal)d,
                decimal m => m,
                int i => i,
                long l => l,
                float f => (decimal)f,
                _ => decimal.TryParse(
                         item?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                     ? parsed
                     : throw new ApplicationException($"Quantidade inválida: {item}"),
            });
        }

        return result.Count == 0 ? null : result;
    }

    private static string? Text(ODataActionParameters parameters, string name)
    {
        // ⚠️ TryGetValue devolve true com valor NULO para parâmetro opcional não informado, e o
        // .ToString() direto estoura. Ver o mesmo cuidado no controller de recusa de carga.
        if (!parameters.TryGetValue(name, out var value))
            return null;

        var text = value?.ToString();

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
