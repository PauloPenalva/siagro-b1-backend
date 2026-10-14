using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Regra 17: a sobra de um firme (NF acima do contrato) sai por dação, pelo operador, com motivo.</summary>
public class FinancialDocumentsDationTests
{
    private static FinancialDocumentsDationService Dation(FirmPayableSeed s) => new(s.Db);

    [Fact]
    public async Task Dation_settles_part_of_a_firm_without_account()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        var line = await Dation(seed).ExecuteAsync(c.Firm!.Key, 1_000m, new DateTime(2026, 10, 20), "Sobra faturada acima do contrato", "ana");

        Assert.Equal(FinancialSettlementOrigin.Dation, line.Origin);
        Assert.Null(line.FinancialAccountCode);
        Assert.Equal(1_000m, line.Amount);
        Assert.Equal(new DateTime(2026, 10, 20), line.SettlementDate);
        Assert.Equal("Sobra faturada acima do contrato", line.Notes);
        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(18_728.33m, firm.OpenAmount);
        Assert.Equal(FinancialDocumentStatus.PartiallySettled, firm.Status);
    }

    [Fact]
    public async Task Partial_composition_plus_dation_closes_the_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync(provisionalAmount: 15_000m);
        var c = await seed.ConciliateAsync();
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key], "ana");

        await Dation(seed).ExecuteAsync(c.Firm.Key, 4_728.33m, DateTime.Today, "Faturado acima do contrato", "ana");

        Assert.Equal(FinancialDocumentStatus.Settled, (await seed.ReloadAsync(c.Firm.Key)).Status);
    }

    [Fact]
    public async Task Dation_is_reversed_by_the_generic_reversal()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var line = await Dation(seed).ExecuteAsync(c.Firm!.Key, 1_000m, DateTime.Today, "Sobra", "ana");

        await new FinancialDocumentsReverseSettlementService(seed.Db).ExecuteAsync(line.Key, "lançada errada", "ana");

        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(0m, firm.SettledAmount);
        Assert.Equal(FinancialDocumentStatus.Open, firm.Status);
    }

    [Fact]
    public async Task Dation_is_refused_on_provisional_and_on_return()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var r = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn);

        foreach (var key in new[] { seed.Provisional.Key, r.Firm!.Key })
        {
            var error = await Assert.ThrowsAsync<ApplicationException>(() =>
                Dation(seed).ExecuteAsync(key, 10m, DateTime.Today, "Sobra", "ana"));
            Assert.Equal("Dação só se aplica a título firme da conciliação fiscal.", error.Message);
        }
    }

    [Fact]
    public async Task Dation_requires_a_reason_and_a_positive_value_within_the_open_amount()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        Assert.Equal("Informe o motivo da dação.", (await Assert.ThrowsAsync<ApplicationException>(() =>
            Dation(seed).ExecuteAsync(c.Firm!.Key, 10m, DateTime.Today, "   ", "ana"))).Message);
        Assert.Equal("O valor da dação deve ser maior que zero.", (await Assert.ThrowsAsync<ApplicationException>(() =>
            Dation(seed).ExecuteAsync(c.Firm!.Key, 0m, DateTime.Today, "Sobra", "ana"))).Message);
        Assert.Equal("O valor da dação (20.000,00) excede o saldo em aberto do título (19.728,33).",
            (await Assert.ThrowsAsync<ApplicationException>(() =>
                Dation(seed).ExecuteAsync(c.Firm!.Key, 20_000m, DateTime.Today, "Sobra", "ana"))).Message);
        Assert.Empty(seed.Context.FinancialSettlements.AsNoTracking());
    }

    /// <summary>Carry-forward da Task 6: o pré-check de cancelamento da NF recusa firme baixado por dação (P1).</summary>
    [Fact]
    public async Task Invoice_cancel_dry_run_refuses_a_firm_settled_by_dation()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await Dation(seed).ExecuteAsync(c.Firm!.Key, 1_000m, DateTime.Today, "Sobra", "ana");

        var error = await Assert.ThrowsAsync<SiagroB1.Domain.Exceptions.DefaultException>(() =>
            new SiagroB1.Application.Services.PurchaseInvoices.PurchaseInvoicesCancelService(seed.Db).EnsureCanCancelAsync(c.Invoice.Key));

        Assert.Equal(
            "O título ST-0001 do vínculo já tem baixa por dação ou encontro de contas: estorne-a antes de alterar a conciliação.",
            error.Message);
    }
}
