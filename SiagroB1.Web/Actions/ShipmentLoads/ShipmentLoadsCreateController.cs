using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Web.Actions.ShipmentLoads;

/// <summary>
/// Criação da carga pelo formulário da Logística.
/// </summary>
/// <remarks>
/// Este formulário tem muitos campos opcionais, o que faz dele o cenário exato de duas
/// armadilhas do OData deste projeto, e as duas estão tratadas aqui:
/// <list type="number">
/// <item><c>ODataActionParameters</c> chega NULO quando o corpo não casa com nenhum parâmetro
/// declarado no EDM — sem a guarda, o <c>TryGetValue</c> estoura NRE e o cliente recebe um 500
/// de corpo vazio, que não diz nada.</item>
/// <item>Parâmetro de string é anulável: <c>TryGetValue</c> devolve <b>true</b> com valor
/// <c>null</c>. Por isso nunca se chama <c>.ToString()</c> direto no objeto devolvido — sempre
/// <c>?.ToString()</c>.</item>
/// </list>
/// </remarks>
public class ShipmentLoadsCreateController(
    ShipmentLoadsCreateService createService
    ) : ODataController
{
    [HttpPost("odata/ShipmentLoadsCreate")]
    public async Task<ActionResult> Create(ODataActionParameters parameters)
    {
        try
        {
            if (parameters == null)
            {
                return BadRequest("Missing required parameters");
            }

            var load = new ShipmentLoad
            {
                BranchCode = Text(parameters, "BranchCode"),
                LoadDate = Date(parameters, "LoadDate") ?? DateTime.Now.Date,
                TruckCode = Text(parameters, "TruckCode"),
                TruckDriverCode = Text(parameters, "TruckDriverCode"),
                TruckDriverName = Text(parameters, "TruckDriverName"),
                CarrierCardCode = Text(parameters, "CarrierCardCode"),
                CarrierName = Text(parameters, "CarrierName"),
                ItemCode = Text(parameters, "ItemCode") ?? string.Empty,
                ItemName = Text(parameters, "ItemName"),
                UnitOfMeasureCode = Text(parameters, "UnitOfMeasureCode") ?? string.Empty,
                WarehouseCode = Text(parameters, "WarehouseCode"),
                WarehouseName = Text(parameters, "WarehouseName"),
                CardCode = Text(parameters, "CardCode"),
                CardName = Text(parameters, "CardName"),
                HasExcess = Flag(parameters, "HasExcess"),
                FreightPrice = Money(parameters, "FreightPrice"),
                Comments = Text(parameters, "Comments"),
            };

            var userName = User.Identity?.Name ?? "Unknown";

            var created = await createService.ExecuteAsync(load, userName);

            return Ok(new { created.Key, created.Code });
        }
        catch (Exception e)
        {
            if (e is KeyNotFoundException or NotFoundException)
            {
                return NotFound();
            }

            return BadRequest(e.Message);
        }
    }

    /// <summary>
    /// Lê um parâmetro de texto tolerando as duas formas de ausência: chave não enviada e chave
    /// enviada com <c>null</c>. Devolve <c>null</c> para vazio, para não gravar string em branco
    /// onde o modelo espera ausência.
    /// </summary>
    internal static string? Text(ODataActionParameters parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value))
            return null;

        var text = value?.ToString();

        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    internal static bool Flag(ODataActionParameters parameters, string name) =>
        parameters.TryGetValue(name, out var value)
        && value is bool flag
        && flag;

    internal static decimal? Money(ODataActionParameters parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value == null)
            return null;

        return value switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            int i => i,
            _ => decimal.TryParse(
                     value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                 ? parsed
                 : null,
        };
    }

    internal static DateTime? Date(ODataActionParameters parameters, string name)
    {
        if (!parameters.TryGetValue(name, out var value) || value == null)
            return null;

        return value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.DateTime,
            _ => DateTime.TryParse(
                     value.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                 ? parsed
                 : null,
        };
    }
}
