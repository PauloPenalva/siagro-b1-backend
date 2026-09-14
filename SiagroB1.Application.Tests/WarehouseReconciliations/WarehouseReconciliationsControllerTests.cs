using Microsoft.AspNetCore.OData.Results;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

/// <summary>
/// O GET por chave precisa devolver um <see cref="SingleResult{T}"/> sobre o IQueryable: com a
/// entidade já materializada, o <c>[EnableQuery]</c> aceita o <c>$expand=Reason</c>, mas devolve
/// <c>"Reason": null</c> — o campo Motivo ficava em branco no detalhe e na aprovação.
/// </summary>
public class WarehouseReconciliationsControllerTests
{
    [Fact]
    public async Task Get_by_key_returns_a_composable_single_result_so_expand_is_applied()
    {
        var ctx = new WarehouseReconciliationsTestContext();
        var reconciliation = await ctx.SeedWithStatusAsync(WarehouseReconciliationStatus.Approved, DateTime.Today);
        var controller = new WarehouseReconciliationsController(
            ctx.Create(), ctx.Update(), new WarehouseReconciliationsGetService(ctx.Db));

        object result = controller.Get(reconciliation.Key);

        var single = Assert.IsType<SingleResult<WarehouseReconciliation>>(result);
        Assert.Equal(reconciliation.Key, Assert.Single(single.Queryable).Key);
    }
}
