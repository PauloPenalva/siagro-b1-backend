using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Regras 18–21: a devolução compensa o firme de origem solto; senão fica em aberto para o operador.</summary>
public class FinancialDocumentsNettingTests
{
    private static FinancialDocumentsNettingService Netting(FirmPayableSeed s) => new(s.Db);

    private static Task<Conciliation> ReturnAsync(FirmPayableSeed s, Guid? originLineKey, decimal kg = 1_500m) =>
        s.ConciliateAsync(kg: kg, invoiceType: PurchaseInvoiceType.Return, transactionType: StorageTransactionType.PurchaseReturn,
            originLineKey: originLineKey, taxNumber: "2001");

    [Fact]
    public async Task Return_born_with_a_loose_origin_is_netted_automatically()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        var r = await ReturnAsync(seed, c.Line.Key);

        var refund = await seed.ReloadAsync(r.Firm!.Key);
        Assert.Equal(2_959.25m, refund.NetAmount);
        Assert.Equal(FinancialDocumentStatus.Settled, refund.Status);
        var pair = await seed.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.Origin == FinancialSettlementOrigin.Netting).ToListAsync();
        Assert.Equal(2, pair.Count);
        Assert.All(pair, s => Assert.Equal(2_959.25m, s.Amount));
        Assert.All(pair, s => Assert.Null(s.FinancialAccountCode));
        Assert.Equal(c.Firm!.Key, pair.Single(s => s.FinancialDocumentKey == refund.Key).RelatedDocumentKey);
        Assert.Equal(refund.Key, pair.Single(s => s.FinancialDocumentKey == c.Firm.Key).RelatedDocumentKey);
        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(16_769.08m, firm.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, firm.Status);
    }

    [Fact]
    public async Task Composed_origin_leaves_the_return_open()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m); // origem composta, mas ainda com saldo
        var c = await seed.ConciliateAsync();
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        var r = await ReturnAsync(seed, c.Line.Key);

        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(r.Firm!.Key)).Status);
        Assert.Empty(seed.Context.FinancialSettlements.AsNoTracking().Where(s => s.Origin == FinancialSettlementOrigin.Netting));
    }

    [Fact]
    public async Task Return_without_origin_stays_open()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        await seed.ConciliateAsync();

        var r = await ReturnAsync(seed, originLineKey: null);

        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(r.Firm!.Key)).Status);
    }

    [Fact]
    public async Task Oldest_origin_is_netted_first()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c1 = await seed.ConciliateAsync();
        var tx2 = await seed.Fiscal.AddTransactionAsync(10_000m, contract: seed.Contract);
        await seed.Reconciliation().SaveAsync(
            [new FiscalReconciliationPairDto { PurchaseInvoiceItemKey = c1.Line.Key!.Value, StorageTransactionKey = tx2.Key, Quantity = 10_000m }],
            "ana");
        var older = await seed.Context.FinancialDocuments.SingleAsync(d => d.StorageTransactionKey == tx2.Key);
        older.CreatedAt = new DateTime(2026, 10, 1);
        c1.Firm!.CreatedAt = new DateTime(2026, 10, 2);
        await seed.Context.SaveChangesAsync();

        var r = await ReturnAsync(seed, c1.Line.Key, kg: 15_000m); // 29.592,50

        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(older.Key)).Status);       // 19.728,33 inteiro
        Assert.Equal(9_864.16m, (await seed.ReloadAsync(c1.Firm.Key)).OpenAmount);                      // 19.728,33 − 9.864,17
        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(r.Firm!.Key)).Status);
    }

    [Fact]
    public async Task Manual_netting_compensates_a_loose_firm_of_the_same_contract()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var r = await ReturnAsync(seed, originLineKey: null);

        await Netting(seed).ExecuteAsync(r.Firm!.Key, c.Firm!.Key, "ana");

        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(r.Firm.Key)).Status);
        Assert.Equal(16_769.08m, (await seed.ReloadAsync(c.Firm.Key)).OpenAmount);
    }

    [Fact]
    public async Task Manual_netting_refusals()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m); // firme composto, ainda com saldo 4.728,33
        var c = await seed.ConciliateAsync();
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");
        var r = await ReturnAsync(seed, originLineKey: null);

        Assert.Equal("Compensar com firme só vale para título de devolução.",
            (await Assert.ThrowsAsync<ApplicationException>(() => Netting(seed).ExecuteAsync(c.Firm.Key, r.Firm!.Key, "ana"))).Message);
        Assert.Equal("O título FN-PROV-1 não é um título firme da conciliação fiscal.",
            (await Assert.ThrowsAsync<ApplicationException>(() => Netting(seed).ExecuteAsync(r.Firm!.Key, seed.Provisional.Key, "ana"))).Message);
        Assert.Equal("O título ST-0001 está composto no título FN-PROV-1: só firme solto compensa a devolução.",
            (await Assert.ThrowsAsync<ApplicationException>(() => Netting(seed).ExecuteAsync(r.Firm!.Key, c.Firm.Key, "ana"))).Message);
    }

    [Fact]
    public async Task Reversing_one_netting_line_reverses_the_pair()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var r = await ReturnAsync(seed, c.Line.Key);
        var line = await seed.Context.FinancialSettlements.AsNoTracking()
            .SingleAsync(s => s.Origin == FinancialSettlementOrigin.Netting && s.FinancialDocumentKey == r.Firm!.Key);

        await new FinancialDocumentsReverseSettlementService(seed.Db).ExecuteAsync(line.Key, "devolução errada", "ana");

        var reversals = await seed.Context.FinancialSettlements.AsNoTracking()
            .Where(s => s.Origin == FinancialSettlementOrigin.Reversal).ToListAsync();
        Assert.Equal(2, reversals.Count);
        Assert.All(reversals, s => Assert.Equal(-2_959.25m, s.Amount));
        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(r.Firm!.Key)).Status);
        var firm = await seed.ReloadAsync(c.Firm!.Key);
        Assert.Equal(0m, firm.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
    }
}
