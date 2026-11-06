using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Application.Services.Market;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;
using SiagroB1.Web.Market;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Cadastro de instrumentos. Os códigos de exemplo têm espaço ("DOL COM"): dentro do $batch a chave chega
/// codificada e o GET/PATCH/DELETE dava 404, por isso toda action com chave normaliza antes. Regra de negócio
/// (BusinessException) vira 400 e falta de MARKET_MANAGE vira 403 (ver <see cref="MarketODataController{T,ID}"/>).
/// </summary>
public class MarketInstrumentsController(MarketInstrumentService service, MarketPermissionGuard guard)
    : MarketODataController<MarketInstrument, string>(service, guard)
{
    protected override string KeyPropertyName => nameof(MarketInstrument.Code);

    [EnableQuery]
    public override Task<ActionResult<MarketInstrument>> Get([FromRoute] string key) =>
        base.Get(ODataKeyNormalizer.Normalize(key));

    public override Task<IActionResult> Put([FromRoute] string key, [FromBody] MarketInstrument entity) =>
        base.Put(ODataKeyNormalizer.Normalize(key), entity);

    public override Task<IActionResult> Delete([FromRoute] string key) =>
        base.Delete(ODataKeyNormalizer.Normalize(key));

    [AcceptVerbs("PATCH", "MERGE")]
    public override Task<IActionResult> Patch([FromODataUri] string key, [FromBody] Delta<MarketInstrument> patch) =>
        base.Patch(ODataKeyNormalizer.Normalize(key), patch);
}
