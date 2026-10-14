using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Modelo dos títulos firmes de compra (spec 2026-10-13): composição, disponível para baixa e bloqueio.</summary>
public class FinancialDocumentCompositionModelTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static FinancialDocument Document(
        FinancialDocumentNature nature, FinancialDocumentOrigin origin, decimal net, decimal settled = 0m, decimal composed = 0m) => new()
    {
        Key = Guid.NewGuid(), Code = "FN000001", CardCode = "F0001", Direction = FinancialDirection.Payable,
        Nature = nature, OriginType = origin, NetAmount = net, SettledAmount = settled, ComposedAmount = composed,
        DueDate = new DateTime(2026, 12, 31),
    };

    [Fact]
    public void New_enum_values_are_appended_with_fixed_numbers()
    {
        Assert.Equal(4, (int)FinancialDocumentNature.PurchaseReturn);
        Assert.Equal(7, (int)FinancialDocumentOrigin.PurchaseFiscalReconciliation);
        Assert.Equal(6, (int)FinancialSettlementOrigin.Dation);
    }

    [Fact]
    public async Task Composition_row_keeps_the_signed_amount()
    {
        var provisional = Document(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation, 1000m);
        var refund = Document(FinancialDocumentNature.PurchaseReturn, FinancialDocumentOrigin.PurchaseFiscalReconciliation, 120.55m);
        _db.Context.FinancialDocuments.AddRange(provisional, refund);
        _db.Context.FinancialDocumentCompositions.Add(new FinancialDocumentComposition
        {
            Key = Guid.NewGuid(), ProvisionalDocumentKey = provisional.Key, ComposedDocumentKey = refund.Key, Amount = -120.55m,
        });
        await _db.Context.SaveChangesAsync();

        var row = await _db.Context.FinancialDocumentCompositions.AsNoTracking().SingleAsync();
        Assert.Equal(-120.55m, row.Amount);
        Assert.Equal(provisional.Key, row.ProvisionalDocumentKey);
        Assert.Equal(refund.Key, row.ComposedDocumentKey);
    }

    [Theory]
    [InlineData(1000.0, 400.0, 300.0, 100.0)]
    [InlineData(1000.0, 400.0, 400.0, 0.0)]
    [InlineData(1000.0, 0.0, 0.0, 0.0)]
    public void Provisional_available_is_composed_minus_settled(double net, double composed, double settled, double expected)
    {
        var provisional = Document(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation,
            (decimal)net, (decimal)settled, (decimal)composed);

        Assert.Equal((decimal)expected, provisional.AvailableToSettleAmount);
        Assert.Equal(expected <= 0, provisional.IsBlockedForSettlement);
    }

    [Fact]
    public void Conciliation_firm_available_is_the_open_amount_and_it_is_always_blocked()
    {
        var firm = Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseFiscalReconciliation, 500m, settled: 120m);

        Assert.Equal(380m, firm.AvailableToSettleAmount);
        Assert.True(firm.IsPurchaseFirmPayable);
        Assert.True(firm.IsBlockedForSettlement);
        Assert.True(Document(FinancialDocumentNature.PurchaseReturn, FinancialDocumentOrigin.PurchaseFiscalReconciliation, 50m)
            .IsBlockedForSettlement);
        // D11: o firme do complemento de valor (origem PurchaseInvoice) é firme de compra igual.
        var complement = Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseInvoice, 80m);
        Assert.True(complement.IsPurchaseFirmPayable);
        Assert.True(complement.IsBlockedForSettlement);
    }

    [Fact]
    public void Washout_receivable_and_advance_stay_free_to_settle()
    {
        var receivable = Document(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout, 500m);
        receivable.Direction = FinancialDirection.Receivable;

        Assert.False(receivable.IsPurchaseFirmPayable);
        Assert.False(receivable.IsBlockedForSettlement);
        Assert.False(Document(FinancialDocumentNature.Advance, FinancialDocumentOrigin.Manual, 500m).IsBlockedForSettlement);
    }

    [Fact]
    public void Edm_exposes_the_new_amounts_but_not_the_composition_set()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();
        var document = model.EntityContainer.FindEntitySet("FinancialDocuments").EntityType;
        var settlement = model.EntityContainer.FindEntitySet("FinancialSettlements").EntityType;

        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.AvailableToSettleAmount)));
        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.ComposedAmount)));
        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.TaxDocumentNumber)));
        Assert.NotNull(document.FindProperty(nameof(FinancialDocument.StorageTransactionCode)));
        Assert.Null(document.FindProperty(nameof(FinancialDocument.IsPurchaseFirmPayable)));
        Assert.NotNull(settlement.FindProperty(nameof(FinancialSettlement.RelatedDocumentKey)));
        Assert.NotNull(settlement.FindProperty(nameof(FinancialSettlement.CompositionKey)));
        Assert.Null(model.EntityContainer.FindEntitySet("FinancialDocumentCompositions"));
    }
}
