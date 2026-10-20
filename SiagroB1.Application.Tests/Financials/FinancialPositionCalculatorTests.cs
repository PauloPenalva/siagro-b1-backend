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

    // ---- Task 2: distribuição do pagamento do provisório (R5, R7, C1) ----

    private static readonly FinancialDocumentOrigin FirmOrigin = FinancialDocumentOrigin.PurchaseFiscalReconciliation;

    private static FinancialDocument Provisional() =>
        Doc(FinancialDocumentNature.Provisional, FinancialDocumentOrigin.PurchaseContractPriceFixation, amount: 1000m);

    private static FinancialDocument FirmDue(DateTime due, decimal amount = 100m,
        FinancialDocumentNature nature = FinancialDocumentNature.Firm)
    {
        var d = Doc(nature, FirmOrigin, amount: amount);
        d.DueDate = due;
        return d;
    }

    // Linha InvoiceOffset gravada no firme pela composição (sempre positiva; devolução usa Math.Abs).
    private static FinancialSettlement Offset(FinancialDocument firm, FinancialDocument provisional, decimal amount,
        DateTime date, Guid? key = null)
    {
        var s = Settle(firm, Math.Abs(amount), date, FinancialSettlementOrigin.InvoiceOffset, key ?? Guid.NewGuid());
        s.RelatedDocumentKey = provisional.Key;
        return s;
    }

    private static FinancialSettlement Pay(FinancialDocument provisional, decimal amount, DateTime date,
        FinancialSettlementOrigin origin = FinancialSettlementOrigin.Manual)
    {
        var s = Settle(provisional, amount, date, origin);
        s.FinancialAccountCode = "CX01";
        return s;
    }

    private static readonly DateTime Composed = new(2026, 10, 3);

    private static decimal OpenOf(FinancialPosition p, FinancialDocument d) =>
        p.Titles.Single(t => t.Document.Key == d.Key).OpenAtBase;

    [Fact]
    public void Composed_firm_stays_open_until_provisional_is_paid()
    {
        var prov = Provisional();
        var firm = FirmDue(new DateTime(2026, 10, 1));
        var p = Compute([prov, firm], [Offset(firm, prov, 100m, Composed)], Base);
        Assert.Equal(100m, OpenOf(p, firm));
    }

    [Fact]
    public void Provisional_payment_distributes_by_due_date()
    {
        var prov = Provisional();
        var f1 = FirmDue(new DateTime(2026, 10, 1));
        var f2 = FirmDue(new DateTime(2026, 10, 5));
        var p = Compute([prov, f2, f1],
            [Offset(f1, prov, 100m, Composed), Offset(f2, prov, 100m, Composed), Pay(prov, 150m, new DateTime(2026, 10, 8))], Base);

        Assert.Equal(0m, OpenOf(p, f1));
        Assert.Equal(50m, OpenOf(p, f2));
        var slices = p.Settlements.Where(s => s.IsDistributed).ToList();
        Assert.Equal(2, slices.Count);
        Assert.All(slices, s => Assert.Equal(new DateTime(2026, 10, 8), s.Date));
        Assert.All(slices, s => Assert.Equal("CX01", s.Source.FinancialAccountCode));
        Assert.Equal(100m, slices.Single(s => s.Document.Key == f1.Key).Amount);
        Assert.Equal(50m, slices.Single(s => s.Document.Key == f2.Key).Amount);
    }

    [Fact]
    public void Payment_after_base_is_not_distributed()
    {
        var prov = Provisional();
        var f1 = FirmDue(new DateTime(2026, 10, 1));
        var f2 = FirmDue(new DateTime(2026, 10, 5));
        var p = Compute([prov, f1, f2],
            [Offset(f1, prov, 100m, Composed), Offset(f2, prov, 100m, Composed), Pay(prov, 150m, new DateTime(2026, 10, 11))], Base);
        Assert.Equal(100m, OpenOf(p, f1));
        Assert.Equal(100m, OpenOf(p, f2));
    }

    [Fact]
    public void Return_is_netted_first()
    {
        var prov = Provisional();
        var f1 = FirmDue(new DateTime(2026, 10, 1));
        var f2 = FirmDue(new DateTime(2026, 10, 5));
        var ret = FirmDue(new DateTime(2026, 10, 2), 30m, FinancialDocumentNature.PurchaseReturn);
        FinancialSettlement[] ledger =
            [Offset(f1, prov, 100m, Composed), Offset(f2, prov, 100m, Composed), Offset(ret, prov, -30m, Composed)];

        var partial = Compute([prov, f1, f2, ret], [.. ledger, Pay(prov, 120m, new DateTime(2026, 10, 8))], Base);
        Assert.Equal(0m, OpenOf(partial, f1));
        Assert.Equal(50m, OpenOf(partial, f2));
        Assert.Equal(-30m, OpenOf(partial, ret));

        var full = Compute([prov, f1, f2, ret], [.. ledger, Pay(prov, 170m, new DateTime(2026, 10, 8))], Base);
        Assert.Equal(0m, OpenOf(full, f1));
        Assert.Equal(0m, OpenOf(full, f2));
        Assert.Equal(0m, OpenOf(full, ret));
    }

    [Fact]
    public void Uncomposed_after_base_still_counts()
    {
        var prov = Provisional();
        var firm = FirmDue(new DateTime(2026, 10, 1));
        var key = Guid.NewGuid();
        var offset = Offset(firm, prov, 100m, Composed, key);
        var reversal = Settle(firm, -100m, new DateTime(2026, 10, 15), FinancialSettlementOrigin.Reversal, key);
        reversal.RelatedDocumentKey = prov.Key;
        var p = Compute([prov, firm], [offset, reversal, Pay(prov, 100m, new DateTime(2026, 10, 8))], Base);
        Assert.Equal(0m, OpenOf(p, firm));
    }

    [Fact]
    public void Advance_application_on_provisional_is_distributed()
    {
        var prov = Provisional();
        var firm = FirmDue(new DateTime(2026, 10, 1));
        var p = Compute([prov, firm],
            [Offset(firm, prov, 100m, Composed), Pay(prov, 100m, new DateTime(2026, 10, 8), FinancialSettlementOrigin.AdvanceApplication)], Base);
        Assert.Equal(0m, OpenOf(p, firm));
        var slice = Assert.Single(p.Settlements, s => s.IsDistributed);
        Assert.Equal(FinancialSettlementOrigin.AdvanceApplication, slice.Source.Origin);
        Assert.Equal(firm.Key, slice.Document.Key);
    }

    [Fact]
    public void Dation_on_firm_counts_directly()
    {
        var firm = FirmDue(new DateTime(2026, 10, 1));
        var p = Compute([firm], [Settle(firm, 20m, new DateTime(2026, 10, 5), FinancialSettlementOrigin.Dation)], Base);
        Assert.Equal(80m, OpenOf(p, firm));
    }

    [Fact]
    public void Pool_reversal_unwinds_from_the_end_of_the_queue()
    {
        var prov = Provisional();
        var f1 = FirmDue(new DateTime(2026, 10, 1));
        var f2 = FirmDue(new DateTime(2026, 10, 5));
        var pay = Pay(prov, 150m, new DateTime(2026, 10, 6));
        var rev = Pay(prov, -150m, new DateTime(2026, 10, 7), FinancialSettlementOrigin.Reversal);
        var pay2 = Pay(prov, 60m, new DateTime(2026, 10, 8));
        var p = Compute([prov, f1, f2],
            [Offset(f1, prov, 100m, Composed), Offset(f2, prov, 100m, Composed), pay, rev, pay2], Base);
        Assert.Equal(40m, OpenOf(p, f1));
        Assert.Equal(100m, OpenOf(p, f2));
        Assert.Contains(p.Settlements, s => s.IsDistributed && s.Document.Key == f2.Key && s.Amount == -50m);
    }
}
