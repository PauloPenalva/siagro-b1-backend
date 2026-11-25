using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Functions.ShipmentLoads;

/// <summary>
/// Prévia do "Vincular NF do SAP" (GAC-1226): tipo do vínculo e alerta de divergência, sem gravar nada.
/// O serviço só é registrado em modo SAPB1; fora dele a função responde 400.
/// </summary>
public class ShipmentLoadsGetSapLinkPreviewController(IServiceProvider services) : ODataController
{
    /// <summary>
    /// Rota declarada à mão, como todas as funções deste projeto: a forma não declarada toma 404.
    /// </summary>
    [HttpGet("odata/ShipmentLoadsGetSapLinkPreview(Key={key},SalesShipmentReleaseKey={salesShipmentReleaseKey},SapDocEntries={sapDocEntries})")]
    public async Task<ActionResult<SapLinkPreviewDto>> Get(
        [FromRoute] Guid key,
        [FromRoute] Guid salesShipmentReleaseKey,
        [FromRoute] string? sapDocEntries)
    {
        // Resolvido sob demanda: só existe no ramo SAPB1 do Program.
        var service = services.GetService<ShipmentLoadsLinkSapInvoiceService>();
        if (service == null)
            return BadRequest("Disponível somente com integração SAP Business One.");

        if (!TryParseDocEntries(sapDocEntries, out var entries))
            return BadRequest("Informe as notas fiscais do SAP a vincular.");

        try
        {
            var assessment = await service.PreviewAsync(key, entries, salesShipmentReleaseKey);

            return Ok(new SapLinkPreviewDto
            {
                LinkType = (int)assessment.Type,
                Message = assessment.Message,
                EffectiveQuantity = assessment.EffectiveQuantity,
                InvoiceQuantity = assessment.InvoiceQuantity,
                QuantityDifference = assessment.QuantityDifference,
                EffectiveValue = assessment.EffectiveValue,
                InvoiceValue = assessment.InvoiceValue,
                ValueDifference = assessment.ValueDifference,
            });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
                return NotFound();

            if (e is DefaultException or BusinessException or ApplicationException or FormatException)
                return BadRequest(e.Message);

            return StatusCode(500, e.Message);
        }
    }

    /// <summary>
    /// <c>SapDocEntries</c> chega como string OData (anulável e com as aspas): inteiros positivos separados por
    /// vírgula. Qualquer elemento vazio ou inválido invalida o conjunto.
    /// </summary>
    public static bool TryParseDocEntries(string? raw, out List<int> entries)
    {
        entries = [];
        var text = raw?.Trim().Trim('\'').Trim();
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var part in text.Split(','))
        {
            if (!int.TryParse(part.Trim(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var number) || number <= 0)
            {
                entries = [];
                return false;
            }

            entries.Add(number);
        }

        return true;
    }
}
