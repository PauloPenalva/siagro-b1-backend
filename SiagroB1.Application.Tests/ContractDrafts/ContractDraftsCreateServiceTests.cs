using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ContractDrafts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ContractDrafts;

public class ContractDraftsCreateServiceTests
{
    private readonly ContractDraftsTestContext _ctx = new();

    [Fact]
    public async Task Creates_a_rendered_snapshot_with_sequence_and_change_log()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();

        var draft = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key,
            ContractDraftType.Contract, "Minuta inicial", "tester", CancellationToken.None);

        Assert.Equal(1, draft.Sequence);
        Assert.Equal("PC-000123", draft.ContractCode);
        Assert.Equal("01", draft.BranchCode);
        Assert.Equal(ContractDraftStatus.Draft, draft.Status);
        Assert.Contains("PC-000123", draft.BodyHtml);
        Assert.Contains("FAZENDA BOA VISTA LTDA", draft.BodyHtml);
        Assert.DoesNotContain("{{", draft.BodyHtml);
        Assert.Contains("\"numero\":\"PC-000123\"", draft.PlaceholdersJson);
        Assert.Equal(contract.Key, draft.PurchaseContractKey);
        Assert.Null(draft.SalesContractKey);

        var log = await _ctx.Db.Context.PurchaseContractsChangeLogs.SingleAsync();
        Assert.Equal(ContractChangeLogFields.Draft, log.Field);
        Assert.Equal("Minuta 1 criada", log.NewValue);
    }

    [Fact]
    public async Task Sequence_increments_per_contract()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync();
        var other = await _ctx.SeedSalesAsync();
        var salesTemplate = await _ctx.SeedTemplateAsync("{{numero}}", ContractTemplateScope.Sales);

        await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key, ContractDraftType.Contract, "1", "t", default);
        var second = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Purchase, contract.Key, template.Key, ContractDraftType.Amendment, "2", "t", default);
        var salesFirst = await _ctx.Create().ExecuteAsync(ContractDraftContractType.Sales, other.Key, salesTemplate.Key, ContractDraftType.Contract, "v", "t", default);

        Assert.Equal(2, second.Sequence);
        Assert.Equal(1, salesFirst.Sequence);
        Assert.Equal(other.Key, salesFirst.SalesContractKey);
    }

    [Fact]
    public async Task Rejects_canceled_contract()
    {
        var template = await _ctx.SeedTemplateAsync();
        var contract = await _ctx.SeedPurchaseAsync(ContractStatus.Canceled);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _ctx.Create().ExecuteAsync(
            ContractDraftContractType.Purchase, contract.Key, template.Key, ContractDraftType.Contract, "x", "t", default));

        Assert.Equal("Contrato cancelado não recebe minuta.", ex.Message);
    }

    [Theory]
    [InlineData(false, ContractTemplateScope.Purchase, "Modelo inativo.")]
    [InlineData(true, ContractTemplateScope.Sales, "Modelo não se aplica a contrato de compra.")]
    public async Task Rejects_inactive_or_incompatible_template(bool active, ContractTemplateScope scope, string message)
    {
        var template = await _ctx.SeedTemplateAsync("{{numero}}", scope, active);
        var contract = await _ctx.SeedPurchaseAsync();

        var ex = await Assert.ThrowsAsync<BusinessException>(() => _ctx.Create().ExecuteAsync(
            ContractDraftContractType.Purchase, contract.Key, template.Key, ContractDraftType.Contract, "x", "t", default));

        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public async Task Missing_contract_or_template_is_not_found()
    {
        var template = await _ctx.SeedTemplateAsync();

        await Assert.ThrowsAsync<NotFoundException>(() => _ctx.Create().ExecuteAsync(
            ContractDraftContractType.Purchase, Guid.NewGuid(), template.Key, ContractDraftType.Contract, "x", "t", default));
    }
}
