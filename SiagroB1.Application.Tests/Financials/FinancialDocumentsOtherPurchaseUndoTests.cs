using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using Fixture = SiagroB1.Application.Tests.Financials.FinancialDocumentsOtherPurchaseTests.Fixture;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-17 §3 (último item): estornar a confirmação ou cancelar o documento desfaz os títulos da outra compra.</summary>
public class FinancialDocumentsOtherPurchaseUndoTests
{
    private static async Task<(Fixture F, PurchaseInvoice Invoice)> ConfirmedAsync()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync(condition: await f.AddConditionAsync("30,60"));
        await f.ConfirmAsync(invoice);
        return (f, invoice);
    }

    private static async Task SettleFirstInstallmentAsync(Fixture f, PurchaseInvoice invoice)
    {
        f.Db.Context.FinancialAccounts.Add(new FinancialAccount
            { Code = "CX01", Name = "Caixa", Type = FinancialAccountType.Cash, Currency = CurrencyType.Brl });
        await f.Db.Context.SaveChangesAsync();
        var first = (await f.TitlesAsync(invoice)).First(d => d.Nature == FinancialDocumentNature.Firm);
        await new FinancialDocumentsSettleService(f.Db)
            .ExecuteAsync(first.Key, "CX01", 100m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");
    }

    [Fact]
    public async Task Reverse_confirm_cancels_open_titles()
    {
        var (f, invoice) = await ConfirmedAsync();
        Assert.Equal(4, (await f.TitlesAsync(invoice)).Count);

        await FinancialDocumentTestServices.PurchaseReverse(f.Db).ExecuteAsync(invoice.Key, "bia");

        var titles = await f.TitlesAsync(invoice);
        Assert.Equal(4, titles.Count);
        Assert.All(titles, d =>
        {
            Assert.Equal(FinancialDocumentStatus.Canceled, d.Status);
            Assert.Equal("bia", d.CanceledBy);
            Assert.NotNull(d.CanceledAt);
            Assert.False(string.IsNullOrWhiteSpace(d.CancellationReason));
        });
        Assert.Equal(InvoiceStatus.Pending, (await f.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Reverse_confirm_with_settled_title_is_refused()
    {
        var (f, invoice) = await ConfirmedAsync();
        await SettleFirstInstallmentAsync(f, invoice);
        var code = (await f.TitlesAsync(invoice)).First(d => d.Nature == FinancialDocumentNature.Firm).Code;

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            FinancialDocumentTestServices.PurchaseReverse(f.Db).ExecuteAsync(invoice.Key, "bia"));

        Assert.Equal($"O título {code} já tem baixa: estorne a baixa no Financeiro antes de desfazer o documento.", error.Message);
        f.Db.Context.ChangeTracker.Clear();
        Assert.Equal(InvoiceStatus.Confirmed, (await f.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key)).InvoiceStatus);
        Assert.All(await f.TitlesAsync(invoice), d => Assert.NotEqual(FinancialDocumentStatus.Canceled, d.Status));
    }

    [Fact]
    public async Task Cancel_cancels_open_titles()
    {
        var (f, invoice) = await ConfirmedAsync();

        await FinancialDocumentTestServices.PurchaseCancel(f.Db).ExecuteAsync(invoice.Key, "bia");

        Assert.All(await f.TitlesAsync(invoice), d => Assert.Equal(FinancialDocumentStatus.Canceled, d.Status));
    }

    [Fact]
    public async Task Cancel_dry_run_refuses_settled_title_and_changes_nothing()
    {
        var (f, invoice) = await ConfirmedAsync();
        await SettleFirstInstallmentAsync(f, invoice);

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            FinancialDocumentTestServices.PurchaseCancel(f.Db).EnsureCanCancelAsync(invoice.Key));

        Assert.Contains("já tem baixa", error.Message);
        Assert.All(await f.TitlesAsync(invoice), d => Assert.NotEqual(FinancialDocumentStatus.Canceled, d.Status));
    }

    [Fact]
    public async Task Confirm_reverse_confirm_recreates_titles()
    {
        var (f, invoice) = await ConfirmedAsync();
        await FinancialDocumentTestServices.PurchaseReverse(f.Db).ExecuteAsync(invoice.Key, "bia");

        await f.ConfirmAsync(invoice);

        var titles = await f.TitlesAsync(invoice);
        Assert.Equal(8, titles.Count);
        Assert.Equal(4, titles.Count(d => d.Status != FinancialDocumentStatus.Canceled));
        Assert.Equal(4, titles.Count(d => d.Status == FinancialDocumentStatus.Canceled));
    }

    [Fact]
    public async Task Double_confirm_does_not_duplicate()
    {
        var (f, invoice) = await ConfirmedAsync();
        var before = (await f.TitlesAsync(invoice)).Select(d => d.Key).ToList();

        var service = FinancialDocumentTestServices.OtherPurchase(f.Db, f.Numbers, "STANDALONE", f.Partners);
        var lines = await f.Db.Context.PurchaseInvoicesItems.AsNoTracking().Where(l => l.PurchaseInvoiceKey == invoice.Key).ToListAsync();
        await service.EnqueueAsync(invoice, lines, "ana");
        await f.Db.SaveChangesAsync();

        Assert.Equal(before.Order(), (await f.TitlesAsync(invoice)).Select(d => d.Key).Order());
    }

    [Fact]
    public async Task Undo_is_a_noop_for_documents_without_other_purchase_titles()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.CreateInvoiceAsync();
        var service = FinancialDocumentTestServices.OtherPurchase(f.Db, f.Numbers, "STANDALONE", f.Partners);

        await service.EnsureCanUndoAsync(invoice.Key);
        service.EnqueueUndo(invoice.Key, "ana");
        await f.Db.SaveChangesAsync();

        Assert.Empty(await f.TitlesAsync(invoice));
    }
}
