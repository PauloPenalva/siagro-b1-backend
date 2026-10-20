using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.Financials;

public class FinancialPositionCalculatorTests
{
    private static readonly DateTime Base = new(2026, 10, 10);

    private static FinancialDocument Doc(
        FinancialDocumentNature nature = FinancialDocumentNature.Firm,
        FinancialDocumentOrigin origin = FinancialDocumentOrigin.Manual,
        DateTime? date = null, decimal amount = 100m,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open) => new()
    {
        Key = Guid.NewGuid(), Code = "FN" + Guid.NewGuid().ToString("N")[..6], CardCode = "C0001",
        Nature = nature, OriginType = origin, DocumentDate = date ?? new DateTime(2026, 10, 1),
        DueDate = new DateTime(2026, 10, 30), NetAmount = amount, Status = status,
        Direction = FinancialDirection.Payable
    };

    private static FinancialSettlement Settle(FinancialDocument doc, decimal amount, DateTime date,
        FinancialSettlementOrigin origin = FinancialSettlementOrigin.Manual, Guid? compositionKey = null) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = doc.Key, Amount = amount, SettlementDate = date,
        Origin = origin, CompositionKey = compositionKey
    };

    private static FinancialPosition Compute(IEnumerable<FinancialDocument> docs, IEnumerable<FinancialSettlement> settlements,
        DateTime baseDate, FinancialPositionMode mode = FinancialPositionMode.Firm) =>
        FinancialPositionCalculator.Compute(
            new FinancialPositionInput(docs.ToList(), settlements.ToList(), []), baseDate, mode);

    [Fact]
    public void Settlement_after_base_date_is_ignored()
    {
        var doc = Doc();
        var position = Compute([doc],
            [Settle(doc, 40m, new DateTime(2026, 10, 5)), Settle(doc, 60m, new DateTime(2026, 10, 11))], Base);

        var title = Assert.Single(position.Titles);
        Assert.Equal(100m, title.Amount);
        Assert.Equal(40m, title.SettledAtBase);
        Assert.Equal(60m, title.OpenAtBase);
        Assert.Single(position.Settlements);
    }

    [Fact]
    public void Reversal_after_base_date_is_ignored()
    {
        var doc = Doc();
        FinancialSettlement[] settlements =
        [
            Settle(doc, 100m, new DateTime(2026, 10, 5)),
            Settle(doc, -100m, new DateTime(2026, 10, 12), FinancialSettlementOrigin.Reversal)
        ];

        Assert.Equal(0m, Assert.Single(Compute([doc], settlements, Base).Titles).OpenAtBase);
        var later = Compute([doc], settlements, new DateTime(2026, 10, 12));
        Assert.Equal(100m, Assert.Single(later.Titles).OpenAtBase);
        Assert.Equal(2, later.Settlements.Count);
        Assert.Contains(later.Settlements, s => s.Amount == -100m);
    }

    [Fact]
    public void Document_created_after_base_date_is_absent()
    {
        var doc = Doc(date: new DateTime(2026, 10, 15));
        Assert.Empty(Compute([doc], [], Base).Titles);
    }

    [Fact]
    public void Time_of_day_is_ignored()
    {
        var doc = Doc(date: new DateTime(2026, 10, 10, 18, 30, 0));
        Assert.Single(Compute([doc], [], Base).Titles);
    }

    [Fact]
    public void Settlement_time_of_day_is_ignored()
    {
        var doc = Doc();
        var position = Compute([doc], [Settle(doc, 30m, new DateTime(2026, 10, 10, 15, 0, 0))], Base);
        Assert.Equal(70m, Assert.Single(position.Titles).OpenAtBase);
    }

    [Fact]
    public void Canceled_is_absent()
    {
        var doc = Doc(status: FinancialDocumentStatus.Canceled);
        Assert.Empty(Compute([doc], [], Base).Titles);
    }

    [Fact]
    public void Firm_mode_excludes_provisional()
    {
        var provisional = Doc(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation);
        var firm = Doc(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseFiscalReconciliation);
        var position = Compute([provisional, firm], [], Base, FinancialPositionMode.Firm);
        Assert.Equal(firm.Key, Assert.Single(position.Titles).Document.Key);
    }

    [Fact]
    public void Provisional_mode_excludes_contract_firm_and_returns()
    {
        var provisional = Doc(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation);
        var firm = Doc(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseFiscalReconciliation);
        var ret = Doc(FinancialDocumentNature.PurchaseReturn, FinancialDocumentOrigin.PurchaseFiscalReconciliation);
        var position = Compute([provisional, firm, ret], [], Base, FinancialPositionMode.Provisional);
        Assert.Equal(provisional.Key, Assert.Single(position.Titles).Document.Key);
    }

    [Fact]
    public void Both_modes_include_advance_manual_and_no_contract()
    {
        FinancialDocument[] docs =
        [
            Doc(FinancialDocumentNature.Advance),
            Doc(FinancialDocumentNature.Firm, FinancialDocumentOrigin.Manual),
            Doc(FinancialDocumentNature.Firm, FinancialDocumentOrigin.OtherPurchaseInvoice),
            Doc(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseContractWashout)
        ];
        Assert.Equal(4, Compute(docs, [], Base, FinancialPositionMode.Firm).Titles.Count);
        Assert.Equal(4, Compute(docs, [], Base, FinancialPositionMode.Provisional).Titles.Count);
    }

    [Fact]
    public void Return_title_amount_is_negative()
    {
        var ret = Doc(FinancialDocumentNature.PurchaseReturn, FinancialDocumentOrigin.PurchaseFiscalReconciliation, amount: 30m);
        var title = Assert.Single(Compute([ret], [], Base).Titles);
        Assert.Equal(-30m, title.Amount);
        Assert.Equal(-30m, title.OpenAtBase);
    }

    [Fact]
    public void Firm_mode_drops_composition_offset_lines_and_their_reversals()
    {
        var doc = Doc(FinancialDocumentNature.Firm, FinancialDocumentOrigin.PurchaseFiscalReconciliation);
        var key = Guid.NewGuid();
        FinancialSettlement[] settlements =
        [
            Settle(doc, 100m, new DateTime(2026, 10, 3), FinancialSettlementOrigin.InvoiceOffset, key),
            Settle(doc, -100m, new DateTime(2026, 10, 4), FinancialSettlementOrigin.Reversal, key),
            Settle(doc, 20m, new DateTime(2026, 10, 5), FinancialSettlementOrigin.Dation)
        ];
        var position = Compute([doc], settlements, Base);
        Assert.Equal(80m, Assert.Single(position.Titles).OpenAtBase);
        Assert.Equal(FinancialSettlementOrigin.Dation, Assert.Single(position.Settlements).Source.Origin);
    }
}
