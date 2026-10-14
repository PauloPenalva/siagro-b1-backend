using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>
/// Regras 23–24 (spec 2026-10-13, títulos firmes): estornar romaneio = excluir a alocação. Com o provisório já pago,
/// recusa; sem baixa, desfaz a composição, remove os vínculos do contrato e cancela os firmes. Devolução e quebra: nada.
/// </summary>
public class FinancialDocumentsFirmUnwindTests
{
    private static PurchaseContractsAllocationDeleteService AllocationDelete(FirmPayableSeed s) =>
        new(s.Db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance);

    private static async Task<Guid> AllocationKeyAsync(FirmPayableSeed s, StorageTransaction tx) =>
        (await s.Context.PurchaseContractsAllocations.AsNoTracking().SingleAsync(a => a.StorageTransactionKey == tx.Key)).Key;

    private static Task ComposeAsync(FirmPayableSeed s, Guid firmKey) =>
        new FinancialDocumentsComposeService(s.Db, new FinancialDocumentChangeLogService(s.Context))
            .ExecuteAsync(s.Provisional.Key, [firmKey], "ana");

    [Fact]
    public async Task Reversing_a_transaction_whose_provisional_has_settlement_is_refused()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        await new FinancialDocumentsSettleService(seed.Db)
            .ExecuteAsync(seed.Provisional.Key, "CX01", 1_000m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");
        var allocationKey = await AllocationKeyAsync(seed, c.Transaction);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            AllocationDelete(seed).ExecuteWithTransactionAsync(allocationKey, "ana"));

        Assert.Equal(
            $"O romaneio {c.Transaction.Code} compõe o título FN-PROV-1, que já tem baixa ou compensação: estorne a baixa antes.",
            error.Message);
        seed.Context.ChangeTracker.Clear();
        Assert.True(await seed.Context.PurchaseContractsAllocations.AnyAsync(a => a.Key == allocationKey));
        Assert.Single(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
        Assert.Single(seed.Context.FinancialDocumentCompositions.AsNoTracking());
    }

    [Fact]
    public async Task Reversing_a_transaction_without_settlement_uncomposes_unlinks_and_cancels_the_firm()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        await ComposeAsync(seed, c.Firm!.Key);
        c.Transaction.FiscalReconciledAt = DateTime.Now;
        c.Transaction.FiscalReconciledBy = "ana";
        await seed.Db.SaveChangesAsync();

        await AllocationDelete(seed).ExecuteWithTransactionAsync(await AllocationKeyAsync(seed, c.Transaction), "bia");

        Assert.Empty(seed.Context.PurchaseContractsAllocations.AsNoTracking());
        Assert.Empty(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
        Assert.Empty(seed.Context.FinancialDocumentCompositions.AsNoTracking());
        var firm = await seed.ReloadAsync(c.Firm.Key);
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Vínculo fiscal desfeito", firm.CancellationReason);
        Assert.Equal("bia", firm.CanceledBy);
        Assert.Equal(0m, firm.SettledAmount);
        Assert.Equal(0m, (await seed.ReloadAsync(seed.Provisional.Key)).ComposedAmount);
        Assert.Null((await seed.Context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == c.Transaction.Key)).FiscalReconciledAt);
    }

    [Fact]
    public async Task Reversing_a_transaction_with_a_loose_firm_unlinks_and_cancels_it()
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();

        await AllocationDelete(seed).ExecuteWithTransactionAsync(await AllocationKeyAsync(seed, c.Transaction), "bia");

        Assert.Equal(FinancialDocumentStatus.Canceled, (await seed.ReloadAsync(c.Firm!.Key)).Status);
        Assert.Empty(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseReturn)]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss)]
    public async Task Return_and_discharge_loss_allocations_do_not_touch_the_return_title(StorageTransactionType type)
    {
        var seed = await FirmPayableSeed.CreateAsync();
        var r = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return, transactionType: type);

        // Caminho em cascata (ExecuteAsync): a tela recusa quebra/sobra (16/17) antes.
        await AllocationDelete(seed).ExecuteAsync(await AllocationKeyAsync(seed, r.Transaction), "bia");

        Assert.Equal(FinancialDocumentStatus.Open, (await seed.ReloadAsync(r.Firm!.Key)).Status);
        Assert.Single(seed.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }
    [Fact]
    public async Task Reversing_a_transaction_whose_firm_carries_a_composed_return_is_refused()
    {
        // Revisão final (I5, Minor): sem baixa, desfazer só o firme deixaria o composto negativo pela devolução.
        var seed = await FirmPayableSeed.CreateAsync();
        var c = await seed.ConciliateAsync();
        var r = await seed.ConciliateAsync(kg: 1_500m, invoiceType: PurchaseInvoiceType.Return,
            transactionType: StorageTransactionType.PurchaseReturn, taxNumber: "2001");
        await new FinancialDocumentsComposeService(seed.Db, new FinancialDocumentChangeLogService(seed.Context))
            .ExecuteAsync(seed.Provisional.Key, [c.Firm!.Key, r.Firm!.Key], "ana");
        var allocationKey = await AllocationKeyAsync(seed, c.Transaction);

        var error = await Assert.ThrowsAsync<ApplicationException>(() =>
            AllocationDelete(seed).ExecuteWithTransactionAsync(allocationKey, "bia"));

        Assert.Equal("Desfaça antes a composição da devolução ST-0002 neste título.", error.Message);
        seed.Context.ChangeTracker.Clear();
        Assert.Equal(2, await seed.Context.FinancialDocumentCompositions.AsNoTracking().CountAsync());
        Assert.Equal(2, await seed.Context.PurchaseFiscalReconciliations.AsNoTracking().CountAsync());
    }
}
