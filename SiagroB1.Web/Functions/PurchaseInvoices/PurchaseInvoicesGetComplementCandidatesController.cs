using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.PurchaseInvoices;

/// <summary>Itens que o complemento de valor próprio pode apontar (spec 2026-10-14) — o diálogo do "Incluir Item".</summary>
public class PurchaseInvoicesGetComplementCandidatesController(
    PurchaseInvoicesGetComplementCandidatesService service) : ODataController
{
    /// <summary>
    /// Rota declarada à mão, como todas as funções deste projeto (a não declarada toma 404). Rotas por atributo
    /// entregam o segmento COM as aspas simples do OData.
    /// </summary>
    [EnableQuery]
    [HttpGet("odata/PurchaseInvoicesGetComplementCandidates(CardCode={cardCode},BranchCode={branchCode})")]
    public async Task<ActionResult<IEnumerable<PurchaseInvoiceComplementCandidateDto>>> Get(
        [FromRoute] string cardCode, [FromRoute] string branchCode) =>
        Ok(await service.ExecuteAsync(cardCode.Trim('\''), branchCode.Trim('\'')));
}
