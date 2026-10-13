using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Retirada de aprovação e exclusão de contrato diante do documento financeiro.
///
/// Caso real (CEAGUI, 13/10/2026): o contrato de venda CV000001 foi aprovado (gerou o provisório
/// FN000001), teve a aprovação retirada — que não mexia no financeiro — e a exclusão do rascunho
/// estourou 500 na FK FINANCIAL_DOCUMENTS → SALES_CONTRACTS. O InMemory NÃO impõe FK, então estes
/// testes conferem que a chave do contrato SOME do documento antes da exclusão, e não apenas que
/// não há exceção.
/// </summary>
public class FinancialDocumentContractWithdrawAndDeleteTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static PurchaseContract NewPurchaseContract(ContractStatus status) => new()
    {
        Key = Guid.NewGuid(),
        Code = "CC000001",
        CardCode = "F0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "01",
        TotalVolume = 1_000m,
        Type = ContractType.Fixed,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = CurrencyType.Brl,
        Status = status,
    };

    private static SalesContract NewSalesContract(ContractStatus status) => new()
    {
        Key = Guid.NewGuid(),
        Code = "CV000001",
        CardCode = "C0001",
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25",
        TotalVolume = 1_000m,
        Price = 3m,
        Type = ContractType.Fixed,
        StandardCashFlowDate = new DateTime(2026, 12, 31),
        StandardCurrency = CurrencyType.Brl,
        Status = status,
    };

    private static FinancialDocument NewDocument(
        string code, FinancialDocumentNature nature, FinancialDocumentStatus status,
        Guid? purchaseContractKey = null, Guid? salesContractKey = null) => new()
    {
        Key = Guid.NewGuid(),
        Code = code,
        CardCode = salesContractKey != null ? "C0001" : "F0001",
        Direction = salesContractKey != null ? FinancialDirection.Receivable : FinancialDirection.Payable,
        Nature = nature,
        Status = status,
        DueDate = new DateTime(2026, 12, 31),
        NetAmount = 1_000m,
        OriginType = salesContractKey != null
            ? FinancialDocumentOrigin.SalesContractPriceFixation
            : FinancialDocumentOrigin.PurchaseContractPriceFixation,
        OriginKey = Guid.NewGuid(),
        OriginDocNumber = salesContractKey != null ? "CV000001" : "CC000001",
        PurchaseContractKey = purchaseContractKey,
        SalesContractKey = salesContractKey,
    };

    private async Task SeedAsync(params object[] entities)
    {
        _db.Context.AddRange(entities);
        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();
    }

    private async Task<FinancialDocument> ReloadAsync(Guid key)
    {
        _db.Context.ChangeTracker.Clear();
        return await _db.Context.FinancialDocuments.SingleAsync(x => x.Key == key);
    }

    // ---------- Retirada de aprovação ----------

    [Fact]
    public async Task Withdrawing_sales_approval_cancels_the_open_provisional_but_not_an_unpaid_advance()
    {
        var contract = NewSalesContract(ContractStatus.Approved);
        var provisional = NewDocument("FN000001", FinancialDocumentNature.Provisional,
            FinancialDocumentStatus.Open, salesContractKey: contract.Key);
        var advance = NewDocument("FN000002", FinancialDocumentNature.Advance,
            FinancialDocumentStatus.Open, salesContractKey: contract.Key);
        await SeedAsync(contract, provisional, advance);

        await new SalesContractsWithdrawApprovalService(_db, new FakeStringLocalizer<Resource>(),
                TestNotificationOutbox.For(_db.Context), FinancialDocumentTestServices.Cancel(_db.Context))
            .ExecuteAsync(contract.Key, "tester");

        var provisionalAfter = await ReloadAsync(provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, provisionalAfter.Status);
        Assert.Equal("Aprovação retirada", provisionalAfter.CancellationReason);
        Assert.Equal(FinancialDocumentStatus.Open, (await ReloadAsync(advance.Key)).Status);
        Assert.Equal(ContractStatus.Draft,
            (await _db.Context.SalesContracts.SingleAsync(x => x.Key == contract.Key)).Status);
    }

    [Fact]
    public async Task Withdrawing_purchase_approval_cancels_the_open_provisional()
    {
        var contract = NewPurchaseContract(ContractStatus.Approved);
        var provisional = NewDocument("FN000001", FinancialDocumentNature.Provisional,
            FinancialDocumentStatus.Open, purchaseContractKey: contract.Key);
        await SeedAsync(contract, provisional);

        await new PurchaseContractsWithdrawApprovalService(_db, new FakeStringLocalizer<Resource>(),
                TestNotificationOutbox.For(_db.Context), FinancialDocumentTestServices.Cancel(_db.Context))
            .ExecuteAsync(contract.Key, "tester");

        var provisionalAfter = await ReloadAsync(provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, provisionalAfter.Status);
        Assert.Equal("Aprovação retirada", provisionalAfter.CancellationReason);
    }

    // ---------- Exclusão do rascunho ----------

    private SalesContractsDeleteService SalesDelete() =>
        new(_db.Context, FinancialDocumentTestServices.Cancel(_db.Context),
            NullLogger<SalesContractsDeleteService>.Instance);

    private PurchaseContractsDeleteService PurchaseDelete() =>
        new(_db.Context, new FakeStringLocalizer<Resource>(), FinancialDocumentTestServices.Cancel(_db.Context),
            NullLogger<PurchaseContractsDeleteService>.Instance);

    /// <summary>
    /// O caso da CEAGUI: rascunho com o provisório ainda ABERTO (retirado antes desta correção) e
    /// um provisório já cancelado. Os dois sobrevivem cancelados — cancela, nunca apaga — e soltam
    /// o contrato, que é excluído.
    /// </summary>
    [Fact]
    public async Task Deleting_a_sales_draft_cancels_and_detaches_its_provisionals()
    {
        var contract = NewSalesContract(ContractStatus.Draft);
        var open = NewDocument("FN000001", FinancialDocumentNature.Provisional,
            FinancialDocumentStatus.Open, salesContractKey: contract.Key);
        var canceled = NewDocument("FN000002", FinancialDocumentNature.Provisional,
            FinancialDocumentStatus.Canceled, salesContractKey: contract.Key);
        await SeedAsync(contract, open, canceled);

        Assert.True(await SalesDelete().ExecuteAsync(contract.Key, "tester"));

        Assert.False(await _db.Context.SalesContracts.AnyAsync(x => x.Key == contract.Key));
        var openAfter = await ReloadAsync(open.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, openAfter.Status);
        Assert.Equal("Contrato excluído", openAfter.CancellationReason);
        Assert.Null(openAfter.SalesContractKey);
        Assert.Equal("CV000001", openAfter.OriginDocNumber);
        var canceledAfter = await ReloadAsync(canceled.Key);
        Assert.Null(canceledAfter.SalesContractKey);
        Assert.Null(canceledAfter.CancellationReason);
    }

    [Fact]
    public async Task Deleting_a_sales_draft_with_an_active_advance_is_refused()
    {
        var contract = NewSalesContract(ContractStatus.Draft);
        var advance = NewDocument("FN000003", FinancialDocumentNature.Advance,
            FinancialDocumentStatus.Open, salesContractKey: contract.Key);
        await SeedAsync(contract, advance);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => SalesDelete().ExecuteAsync(contract.Key, "tester"));

        Assert.Contains("FN000003", ex.Message);
        Assert.True(await _db.Context.SalesContracts.AnyAsync(x => x.Key == contract.Key));
        var advanceAfter = await ReloadAsync(advance.Key);
        Assert.Equal(contract.Key, advanceAfter.SalesContractKey);
        Assert.Equal(FinancialDocumentStatus.Open, advanceAfter.Status);
    }

    [Fact]
    public async Task Deleting_a_purchase_draft_cancels_and_detaches_its_provisionals()
    {
        var contract = NewPurchaseContract(ContractStatus.Draft);
        var open = NewDocument("FN000001", FinancialDocumentNature.Provisional,
            FinancialDocumentStatus.Open, purchaseContractKey: contract.Key);
        await SeedAsync(contract, open);

        Assert.True(await PurchaseDelete().ExecuteAsync(contract.Key, "tester"));

        Assert.False(await _db.Context.PurchaseContracts.AnyAsync(x => x.Key == contract.Key));
        var openAfter = await ReloadAsync(open.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, openAfter.Status);
        Assert.Null(openAfter.PurchaseContractKey);
    }

    [Fact]
    public async Task Deleting_a_purchase_draft_with_an_active_advance_is_refused()
    {
        var contract = NewPurchaseContract(ContractStatus.Draft);
        var advance = NewDocument("FN000003", FinancialDocumentNature.Advance,
            FinancialDocumentStatus.Open, purchaseContractKey: contract.Key);
        await SeedAsync(contract, advance);

        await Assert.ThrowsAsync<DefaultException>(() => PurchaseDelete().ExecuteAsync(contract.Key, "tester"));

        Assert.True(await _db.Context.PurchaseContracts.AnyAsync(x => x.Key == contract.Key));
        Assert.Equal(contract.Key, (await ReloadAsync(advance.Key)).PurchaseContractKey);
    }
}
