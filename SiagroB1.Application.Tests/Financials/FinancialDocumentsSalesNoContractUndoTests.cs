using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using Fixture = SiagroB1.Application.Tests.Financials.FinancialDocumentsSalesNoContractTests.Fixture;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-19 parte 1: cancelar ou estornar o documento de saída desfaz os títulos sem contrato.</summary>
public class FinancialDocumentsSalesNoContractUndoTests
{
    private static async Task<(Fixture F, SalesInvoice Invoice)> ConfirmedAsync()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await f.NoContractInvoiceAsync(condition: await f.AddConditionAsync("30,60"));
        await f.ConfirmAsync(invoice);
        return (f, invoice);
    }

    [Fact]
    public async Task Cancel_cancels_open_no_contract_receivables()
    {
        var (f, invoice) = await ConfirmedAsync();

        await f.Seed.Cancel(f.Service()).ExecuteAsync(invoice.Key, "bia");

        var titles = await f.TitlesAsync(invoice);
        Assert.Equal(2, titles.Count);
        Assert.All(titles, t =>
        {
            Assert.Equal(FinancialDocumentStatus.Canceled, t.Status);
            Assert.Equal("Documento de saída cancelado ou estornado", t.CancellationReason);
        });
    }

    [Fact]
    public async Task Reverse_confirm_cancels_open_no_contract_receivables()
    {
        var (f, invoice) = await ConfirmedAsync();

        await f.Seed.ReverseConfirm(f.Service()).ExecuteAsync(invoice.Key, "bia");

        Assert.All(await f.TitlesAsync(invoice), t => Assert.Equal(FinancialDocumentStatus.Canceled, t.Status));
    }

    [Fact]
    public async Task Undo_refused_when_no_contract_receivable_settled()
    {
        var (f, invoice) = await ConfirmedAsync();
        var first = (await f.TitlesAsync(invoice)).First();
        await new FinancialDocumentsSettleService(f.Seed.Db)
            .ExecuteAsync(first.Key, "CX01", 100m, DateTime.Today, 0m, 0m, 0m, null, null, "ana");

        await Assert.ThrowsAsync<DefaultException>(() => f.Seed.ReverseConfirm(f.Service()).ExecuteAsync(invoice.Key, "bia"));
        await Assert.ThrowsAsync<DefaultException>(() => f.Seed.Cancel(f.Service()).ExecuteAsync(invoice.Key, "bia"));

        var stored = await f.Seed.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, stored.InvoiceStatus);
        Assert.All(await f.TitlesAsync(invoice), t => Assert.NotEqual(FinancialDocumentStatus.Canceled, t.Status));
    }

    [Fact]
    public async Task Manual_cancel_of_no_contract_receivable_is_refused()
    {
        var (f, invoice) = await ConfirmedAsync();
        var first = (await f.TitlesAsync(invoice)).First();

        await Assert.ThrowsAsync<ApplicationException>(() =>
            FinancialDocumentTestServices.Cancel(f.Seed.Context).ExecuteAsync(first.Key, "teste", "bia"));
    }
}
