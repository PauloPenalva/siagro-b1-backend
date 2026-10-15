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
        Ok(await service.ExecuteAsync(Unquote(cardCode), Unquote(branchCode)));

    /// <summary>
    /// Literal de string do OData: uma aspa de cada ponta (não Trim, que levaria o apóstrofo final do código) e o
    /// apóstrofo interno, que a tela manda dobrado ('D''AGUA'), desfeito.
    /// </summary>
    private static string Unquote(string literal)
    {
        var value = literal.Length >= 2 && literal[0] == '\'' && literal[^1] == '\'' ? literal[1..^1] : literal;
        return value.Replace("''", "'");
    }
}
