using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using SiagroB1.Application.Services.Market;
using SiagroB1.Application.Services.Market.Hedges;
using SiagroB1.Domain.Entities.Market;
using SiagroB1.Web.Base;

namespace SiagroB1.Web.Controllers;

/// <summary>
/// Contas de corretoras e bancos. Além do conjunto próprio (<c>MarketCounterpartyAccounts</c>), expõe a navegação
/// <c>MarketCounterparties({id})/Accounts</c> no mesmo molde das linhas da tabela de preços.
/// </summary>
public class MarketCounterpartyAccountsController(MarketCounterpartyAccountService service, MarketPermissionGuard guard)
    : MarketODataController<MarketCounterpartyAccount, int>(service, guard)
{
    [HttpPost("odata/MarketCounterparties({counterpartyId})/Accounts")]
    public Task<IActionResult> PostNested([FromODataUri] int counterpartyId, [FromBody] MarketCounterpartyAccount entity)
    {
        entity.CounterpartyId = counterpartyId;
        return Post(entity);
    }

    [HttpGet("odata/MarketCounterparties({counterpartyId})/Accounts")]
    [EnableQuery]
    public ActionResult<IQueryable<MarketCounterpartyAccount>> GetNested([FromODataUri] int counterpartyId) =>
        Ok(service.QueryByCounterparty(counterpartyId));

    [HttpGet("odata/MarketCounterparties/{counterpartyId}/Accounts({accountId})")]
    [HttpGet("odata/MarketCounterparties({counterpartyId})/Accounts({accountId})")]
    public async Task<IActionResult> GetNestedByKey([FromODataUri] int counterpartyId, [FromODataUri] int accountId)
    {
        var account = await service.GetByIdAsync(accountId);
        return account is null || account.CounterpartyId != counterpartyId ? NotFound() : Ok(account);
    }

    [HttpPatch("odata/MarketCounterparties({counterpartyId})/Accounts({accountId})")]
    public async Task<IActionResult> PatchNested(
        [FromODataUri] int counterpartyId, [FromODataUri] int accountId, [FromBody] Delta<MarketCounterpartyAccount> patch) =>
        await BelongsToAsync(counterpartyId, accountId) ? await Patch(accountId, patch) : NotFound();

    [HttpDelete("odata/MarketCounterparties({counterpartyId})/Accounts({accountId})")]
    public async Task<IActionResult> DeleteNested([FromODataUri] int counterpartyId, [FromODataUri] int accountId) =>
        await BelongsToAsync(counterpartyId, accountId) ? await Delete(accountId) : NotFound();

    // Rota aninhada: a conta precisa pertencer à contraparte da URL.
    private async Task<bool> BelongsToAsync(int counterpartyId, int accountId) =>
        (await service.GetByIdAsync(accountId))?.CounterpartyId == counterpartyId;
}
