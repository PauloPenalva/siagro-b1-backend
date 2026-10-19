using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Financials;

/// <summary>Spec 2026-10-15, pontos de chamada 1, 3 e 4: confirmar, estornar e cancelar o documento de saída.</summary>
public class SalesFirmReceivableHooksTests
{
    [Fact]
    public async Task Confirming_an_avulso_document_creates_the_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();

        var (_, line) = await seed.ConfirmedAsync();

        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        Assert.Equal(SalesFirmReceivableSeed.DefaultLineAmount, firm.NetAmount);
    }

    [Fact]
    public async Task Reversing_the_confirmation_cancels_the_loose_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();

        await seed.ReverseConfirm().ExecuteAsync(invoice.Key, "bia");

        var firm = Assert.Single(await seed.TitlesOfAsync(line, includeCanceled: true));
        Assert.Equal(FinancialDocumentStatus.Canceled, firm.Status);
        Assert.Equal("Documento de saída cancelado ou estornado", firm.CancellationReason);
    }

    [Fact]
    public async Task Reversing_with_a_composed_firm_is_refused_and_the_document_stays_confirmed()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);

        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.ReverseConfirm().ExecuteAsync(invoice.Key, "bia"));

        Assert.Equal(
            $"O título {firm.Code} do documento de saída DS000001 já está composto no título FN-PROV-V1: desfaça a composição antes de cancelar ou estornar o documento.",
            error.Message);
        var saved = await seed.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
    }

    [Fact]
    public async Task Reconfirming_after_reversal_creates_a_new_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();
        await seed.ReverseConfirm().ExecuteAsync(invoice.Key, "bia");

        await seed.Confirm().ExecuteAsync(invoice.Key, "ana");

        var titles = await seed.TitlesOfAsync(line, includeCanceled: true);
        Assert.Equal(2, titles.Count);
        Assert.Single(titles, t => t.Status != FinancialDocumentStatus.Canceled);
    }

    [Fact]
    public async Task Cancelling_the_document_cancels_the_loose_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();

        await seed.Cancel().ExecuteAsync(invoice.Key, "bia");

        Assert.Equal(FinancialDocumentStatus.Canceled,
            Assert.Single(await seed.TitlesOfAsync(line, includeCanceled: true)).Status);
    }

    [Fact]
    public async Task Pre_sefaz_dry_run_refuses_a_document_with_a_composed_firm()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (invoice, line) = await seed.ConfirmedAsync();
        var firm = Assert.Single(await seed.TitlesOfAsync(line));
        await seed.ComposeAsync(firm);

        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Cancel().EnsureCanCancelAsync(invoice.Key));

        Assert.Contains("possui baixa financeira", error.Message); // trava única de baixa responde primeiro
    }

    [Fact]
    public async Task Confirming_a_return_creates_the_sales_return_netted_against_the_origin()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.ConfirmedAsync();
        var firm = Assert.Single(await seed.TitlesOfAsync(originLine));
        var (returnInvoice, returnLine) = await seed.AddInvoiceAsync(quantity: 100m, type: SalesInvoiceType.Return,
            originInvoiceKey: origin.Key, originItemKey: originLine.Key, number: "DS000002");

        await seed.Confirm().ExecuteAsync(returnInvoice.Key, "ana");

        var refund = Assert.Single(await seed.TitlesOfAsync(returnLine));
        Assert.Equal(FinancialDocumentNature.SalesReturn, refund.Nature);
        Assert.Equal(9_000m, refund.NetAmount);
        Assert.Equal(9_000m, (await seed.ReloadAsync(firm.Key)).SettledAmount);
    }

    [Fact]
    public async Task Reversing_a_return_that_netted_the_origin_is_refused()
    {
        var seed = await SalesFirmReceivableSeed.CreateAsync();
        var (origin, originLine) = await seed.ConfirmedAsync();
        var (returnInvoice, returnLine) = await seed.AddInvoiceAsync(quantity: 100m, type: SalesInvoiceType.Return,
            originInvoiceKey: origin.Key, originItemKey: originLine.Key, number: "DS000002");
        await seed.Confirm().ExecuteAsync(returnInvoice.Key, "ana");
        var refund = Assert.Single(await seed.TitlesOfAsync(returnLine));

        var error = await Assert.ThrowsAsync<DefaultException>(() =>
            seed.ReverseConfirm().ExecuteAsync(returnInvoice.Key, "bia"));

        Assert.Equal(
            $"O título {refund.Code} do documento de saída DS000002 já tem baixa por dação ou encontro de contas: estorne-a antes de cancelar ou estornar o documento.",
            error.Message);
    }
}
