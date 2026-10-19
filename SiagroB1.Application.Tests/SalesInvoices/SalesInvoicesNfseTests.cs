using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using Fixture = SiagroB1.Application.Tests.Financials.FinancialDocumentsSalesNoContractTests.Fixture;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>Spec 2026-10-19 parte 2: NFS = documento de saída comum, número/série/data digitados, sem transmissão.</summary>
public class SalesInvoicesNfseTests
{
    private static Task<SalesInvoice> NfseAsync(Fixture f, string? number = "123", string? series = "A", string card = "C0001") =>
        f.NoContractInvoiceAsync(tweak: i =>
        {
            i.TaxDocumentKind = TaxDocumentKind.Nfse;
            i.TaxDocumentNumber = number;
            i.TaxDocumentSeries = series;
            i.CardCode = card;
        });

    [Theory]
    [InlineData(null, "A")]
    [InlineData("123", null)]
    [InlineData(" ", "A")]
    public async Task Confirm_nfse_without_number_series_or_date_is_refused(string? number, string? series)
    {
        var f = await Fixture.CreateAsync();
        var invoice = await NfseAsync(f, number, series);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => f.ConfirmAsync(invoice));

        Assert.Equal("Informe número, série e data de emissão da NFS.", ex.Message);
    }

    [Fact]
    public async Task Confirm_nfse_without_date_is_refused()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await NfseAsync(f);
        invoice.InvoiceDate = null;
        await f.Seed.Context.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => f.ConfirmAsync(invoice));
    }

    [Fact]
    public async Task Confirm_nfse_keeps_typed_number_and_no_nfe_status()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await NfseAsync(f);

        await f.ConfirmAsync(invoice);

        var stored = await f.Seed.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, stored.InvoiceStatus);
        Assert.Equal("123", stored.TaxDocumentNumber);
        Assert.Equal(NfeStatus.None, stored.NfeStatus);
        Assert.Null(stored.ChaveNFe);
    }

    [Fact]
    public async Task Duplicate_nfse_same_partner_series_number_is_refused()
    {
        var f = await Fixture.CreateAsync();
        await f.ConfirmAsync(await NfseAsync(f));
        var second = await NfseAsync(f);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => f.ConfirmAsync(second));

        Assert.StartsWith("Já existe a NFS A/123 para este parceiro", ex.Message);
    }

    [Fact]
    public async Task Same_number_other_partner_is_allowed()
    {
        var f = await Fixture.CreateAsync();
        await f.ConfirmAsync(await NfseAsync(f));

        await f.ConfirmAsync(await NfseAsync(f, card: "C0002"));
    }

    [Fact]
    public async Task Same_number_of_cancelled_document_is_allowed()
    {
        var f = await Fixture.CreateAsync();
        var first = await NfseAsync(f);
        await f.ConfirmAsync(first);
        await f.Seed.Cancel(f.Service()).ExecuteAsync(first.Key, "ana");

        await f.ConfirmAsync(await NfseAsync(f));
    }

    [Fact]
    public async Task Confirm_nfse_with_financial_nature_creates_receivable()
    {
        var f = await Fixture.CreateAsync();
        var invoice = await NfseAsync(f);

        await f.ConfirmAsync(invoice);

        Assert.Equal(1000m, (await f.TitlesAsync(invoice)).Sum(t => t.NetAmount));
    }
}
