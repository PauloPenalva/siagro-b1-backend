using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.WarehouseReconciliations;

/// <summary>
/// Grava a distribuição da perda entre liberações (GAC-1164 §9.4). Substitui o conjunto inteiro; uma
/// lista vazia é válida e limpa a distribuição.
/// </summary>
/// <remarks>
/// As chaves e as quantidades chegam como arrays PARALELOS — mesmo padrão de
/// <c>ShipmentLoadsRefuseController</c>, de onde a leitura das coleções foi copiada.
/// </remarks>
public class WarehouseReconciliationsDistributeLossController(
    WarehouseReconciliationsDistributeLossService service
    ) : ODataController
{
    [HttpPost("odata/WarehouseReconciliationsDistributeLoss")]
    public async Task<ActionResult> PostAsync(ODataActionParameters parameters)
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

            var releaseKeys = parameters.TryGetValue("ShipmentReleaseKeys", out var keysObj) &&
                               keysObj is IEnumerable<Guid> keysSequence
                ? keysSequence.ToList()
                : [];

            var quantities = Quantities(parameters, "Quantities") ?? [];

            if (releaseKeys.Count != quantities.Count)
            {
                return BadRequest(
                    "ShipmentReleaseKeys e Quantities precisam ter o mesmo tamanho.");
            }

            var lines = releaseKeys
                .Select((releaseKey, index) => new WarehouseReconciliationLossLine(releaseKey, quantities[index]))
                .ToList();

            var userName = User.Identity?.Name ?? "Unknown";

            await service.ExecuteAsync(Guid.Parse(keyObj.ToString()!), lines, userName);

            return Ok();
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

    /// <summary>
    /// Coleção de <c>Edm.Double</c>. O OData entrega <c>List&lt;double&gt;</c> quando o JSON traz
    /// decimais, mas um array de INTEIROS (<c>[40, 15]</c>) pode chegar como
    /// <c>IEnumerable&lt;object&gt;</c> com <c>int</c> dentro — e o cast direto para
    /// <c>IEnumerable&lt;double&gt;</c> devolveria lista vazia SEM erro nenhum, fazendo a distribuição
    /// não gravar nada.
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
