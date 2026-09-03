using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Registra a recusa/devolução de uma carga já faturada.
/// </summary>
/// <remarks>
/// As chaves e as quantidades chegam como arrays PARALELOS — ver o comentário da ação em
/// <c>ODataConfigurations</c>. Contagens diferentes são recusadas aqui, antes do serviço: é um
/// erro de montagem do payload, não de negócio, e sem esta checagem viraria um pareamento
/// silenciosamente errado entre documento e quantidade.
/// </remarks>
public class ShipmentLoadsRefuseController(
    ShipmentLoadsRefuseService refuseService
    ) : ODataController
{
    [HttpPost("odata/ShipmentLoadsRefuse")]
    public async Task<ActionResult> Refuse(ODataActionParameters parameters)
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

            if (!parameters.TryGetValue("SalesInvoiceKeys", out var keysObj) ||
                keysObj is not IEnumerable<Guid> invoiceKeys)
            {
                return BadRequest("Selecione ao menos um documento de saída para devolver.");
            }

            var quantities = Quantities(parameters, "Quantities");

            if (quantities == null)
            {
                return BadRequest("Informe a quantidade a devolver de cada documento de saída.");
            }

            var keyList = invoiceKeys.ToList();

            if (keyList.Count != quantities.Count)
            {
                return BadRequest(
                    "A lista de documentos e a de quantidades têm tamanhos diferentes.");
            }

            var destination = ParseDestination(Text(parameters, "Destination"));

            if (destination == null)
            {
                return BadRequest("Informe o destino da mercadoria recusada.");
            }

            var request = new RefusalRequest(
                ShipmentLoadKey: Guid.Parse(keyObj.ToString()!),
                Lines: keyList
                    .Select((invoiceKey, index) => new RefusalLine(invoiceKey, quantities[index]))
                    .ToList(),
                Destination: destination.Value,
                DestinationWarehouseCode: Text(parameters, "DestinationWarehouseCode"),
                Reason: Text(parameters, "Reason") ?? string.Empty);

            var userName = User.Identity?.Name ?? "Unknown";

            var load = await refuseService.ExecuteAsync(request, userName);

            return Ok(new
            {
                load.Key,
                load.Code,
                load.Status,
                load.TotalQuantity,
                load.InvoicedQuantity,
                load.ReturnedToWarehouseQuantity,
                load.AvailableQuantity,
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            // A carga tem [Timestamp] RowVersion: um faturamento parcial concorrente derruba
            // esta recusa aqui. Sem a tradução, o usuário recebe a mensagem crua do EF.
            return BadRequest(
                "A carga foi alterada por outro usuário enquanto a recusa era registrada. " +
                "Reabra a tela e tente novamente.");
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

    private static string? Text(ODataActionParameters parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value))
            return null;

        var text = value?.ToString();

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>
    /// Coleção de <c>Edm.Double</c>. O OData entrega <c>List&lt;double&gt;</c> quando o JSON traz
    /// decimais, mas um array de INTEIROS (<c>[40, 15]</c>) pode chegar como
    /// <c>IEnumerable&lt;object&gt;</c> com <c>int</c> dentro — e o cast direto para
    /// <c>IEnumerable&lt;double&gt;</c> devolveria lista vazia SEM erro nenhum, fazendo a recusa
    /// não devolver nada.
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

        return result;
    }
}
