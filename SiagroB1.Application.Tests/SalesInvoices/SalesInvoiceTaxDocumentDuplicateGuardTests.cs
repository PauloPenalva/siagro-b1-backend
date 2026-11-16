using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Trava compartilhada entre "informar NF" e "vincular NF do SAP": número + série únicos por
/// filial, chave de acesso única globalmente, ignorando Cancelados e Retornados.
/// </summary>
public class SalesInvoiceTaxDocumentDuplicateGuardTests
{
    private const string Chave = "35250712345678000199550010000000011000000017";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesInvoiceTaxDocumentDuplicateGuard Guard() => new(_db.Context);

    private async Task<SalesInvoice> SeedAsync(
        string invoiceNumber = "000000001",
        string branchCode = "F001",
        InvoiceStatus status = InvoiceStatus.Confirmed,
        string? number = null, string? series = null, string? chave = null)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            BranchCode = branchCode,
            CardCode = "C0001",
            InvoiceStatus = status,
            InvoiceType = SalesInvoiceType.Normal,
            TaxDocumentNumber = number,
            TaxDocumentSeries = series,
            ChaveNFe = chave,
        };
        _db.Context.SalesInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Refuses_a_duplicated_number_and_series_in_the_same_branch()
    {
        await SeedAsync(invoiceNumber: "000000009", number: "123", series: "1");

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Guard().EnsureUniqueAsync("F001", "123", "1", null, null));

        Assert.Equal("Nota fiscal 123, série 1 já informada no documento de saída 000000009.", error.Message);
    }

    [Fact]
    public async Task Allows_the_same_number_and_series_in_another_branch()
    {
        await SeedAsync(branchCode: "F002", number: "123", series: "1");

        await Guard().EnsureUniqueAsync("F001", "123", "1", null, null);
    }

    [Theory]
    [InlineData(InvoiceStatus.Cancelled)]
    [InlineData(InvoiceStatus.Returned)]
    public async Task Ignores_cancelled_and_returned_invoices(InvoiceStatus status)
    {
        await SeedAsync(status: status, number: "123", series: "1", chave: Chave);

        await Guard().EnsureUniqueAsync("F001", "123", "1", Chave, null);
    }

    [Fact]
    public async Task Refuses_a_duplicated_access_key_in_any_branch()
    {
        await SeedAsync(invoiceNumber: "000000007", branchCode: "F002", chave: Chave);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Guard().EnsureUniqueAsync("F001", null, null, Chave, null));

        Assert.Equal($"Chave de acesso {Chave} já informada no documento de saída 000000007.", error.Message);
    }

    [Fact]
    public async Task Ignores_the_invoice_being_edited()
    {
        var own = await SeedAsync(number: "123", series: "1", chave: Chave);

        await Guard().EnsureUniqueAsync("F001", "123", "1", Chave, own.Key);
    }

    [Fact]
    public async Task Blank_values_never_collide()
    {
        await SeedAsync();

        await Guard().EnsureUniqueAsync("F001", null, null, null, null);
    }
}
