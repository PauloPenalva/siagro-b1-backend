using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// O recalculador continua escritor único (SettledAmount, Status) e passa a escrever ComposedAmount do provisório.
/// Enxerga linhas ainda não salvas: é o que deixa composição, desfazer e encontro de contas num SaveChanges só.
/// </summary>
public class FinancialDocumentsRecalculateComposedTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FinancialDocument Provisional(decimal net = 1000m) => new()
    {
        Key = Guid.NewGuid(), Code = "FN-PROV", CardCode = "F0001", Direction = FinancialDirection.Payable,
        Nature = FinancialDocumentNature.Provisional, NetAmount = net, DueDate = new DateTime(2026, 12, 31),
        OriginType = FinancialDocumentOrigin.PurchaseContractPriceFixation, OriginKey = Guid.NewGuid(),
    };

    private static FinancialDocumentComposition Row(Guid provisionalKey, decimal amount) => new()
    {
        Key = Guid.NewGuid(), ProvisionalDocumentKey = provisionalKey, ComposedDocumentKey = Guid.NewGuid(), Amount = amount,
    };

    private static FinancialSettlement Settlement(Guid documentKey, decimal amount) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = documentKey, Amount = amount, SettlementDate = DateTime.Today,
        Origin = FinancialSettlementOrigin.Manual,
    };

    [Fact]
    public async Task Composed_amount_is_the_signed_sum_of_the_composition_rows()
    {
        var provisional = Provisional();
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.FinancialDocumentCompositions.AddRange(Row(provisional.Key, 300m), Row(provisional.Key, -50m));
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);

        Assert.Equal(250m, provisional.ComposedAmount);
        Assert.Equal(250m, provisional.AvailableToSettleAmount);
    }

    [Fact]
    public async Task Pending_rows_are_counted_before_save()
    {
        var provisional = Provisional();
        var persisted = Row(provisional.Key, 400m);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.FinancialDocumentCompositions.Add(persisted);
        await _db.Context.SaveChangesAsync();

        _db.Context.FinancialDocumentCompositions.Remove(persisted);                 // Deleted: ainda no banco
        _db.Context.FinancialDocumentCompositions.Add(Row(provisional.Key, 100m));   // Added: ainda fora dele
        _db.Context.FinancialSettlements.Add(Settlement(provisional.Key, 60m));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);

        Assert.Equal(100m, provisional.ComposedAmount);
        Assert.Equal(60m, provisional.SettledAmount);
        Assert.Equal(40m, provisional.AvailableToSettleAmount);
    }

    [Fact]
    public async Task Unsaved_document_is_found_and_recalculated()
    {
        var provisional = Provisional(500m);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.FinancialSettlements.Add(Settlement(provisional.Key, 200m));

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);

        Assert.Equal(200m, provisional.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, provisional.Status);
    }

    [Fact]
    public async Task Provisional_is_settled_only_when_the_contracted_value_is_paid()
    {
        var provisional = Provisional(1000m);
        _db.Context.FinancialDocuments.Add(provisional);
        _db.Context.FinancialDocumentCompositions.Add(Row(provisional.Key, 1000m));
        _db.Context.FinancialSettlements.Add(Settlement(provisional.Key, 600m));
        await _db.Context.SaveChangesAsync();

        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, provisional.Status);

        _db.Context.FinancialSettlements.Add(Settlement(provisional.Key, 400m));
        await FinancialDocumentsRecalculateBalanceService.RecalculateAsync(_db.Context, provisional.Key);
        Assert.Equal(FinancialDocumentStatus.Settled, provisional.Status);
    }
}
