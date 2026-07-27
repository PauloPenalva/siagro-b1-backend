using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Trava de duplicidade da NF-e no documento de saída: número + série são únicos por filial
/// (ignorando cancelados), a chave de acesso é única globalmente (idem), e nenhum dos dois
/// checks pode enxergar o próprio documento em edição nem colidir por valor em branco.
/// </summary>
public class SalesInvoicesSetDocumentNumberServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesInvoicesSetDocumentNumberService Service() => new(_db);

    private async Task<SalesInvoice> SeedInvoiceAsync(
        string invoiceNumber = "000000001",
        string branchCode = "F001",
        InvoiceStatus status = InvoiceStatus.Confirmed,
        string? taxDocumentNumber = null,
        string? taxDocumentSeries = null,
        string? chaveNFe = null)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            BranchCode = branchCode,
            CardCode = "C0001",
            InvoiceStatus = status,
            InvoiceType = SalesInvoiceType.Normal,
            TaxDocumentNumber = taxDocumentNumber,
            TaxDocumentSeries = taxDocumentSeries,
            ChaveNFe = chaveNFe,
        };
        _db.Context.SalesInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        return invoice;
    }

    private const string Chave = "35250712345678000199550010000000011000000017";

    /// <summary>
    /// Queixa 1: o diálogo pré-preenche os campos com os valores do próprio documento, então
    /// reconfirmar sem alterar nada não pode ser tratado como duplicidade.
    /// </summary>
    [Fact]
    public async Task ReInforming_TheSameDocumentWithItsOwnData_Succeeds()
    {
        var invoice = await SeedInvoiceAsync(
            taxDocumentNumber: "123456", taxDocumentSeries: "1", chaveNFe: Chave);

        await Service().ExecuteAsync(invoice.Key, "123456", "1", Chave, "joao");

        Assert.Equal("123456", invoice.TaxDocumentNumber);
        Assert.Equal("1", invoice.TaxDocumentSeries);
        Assert.Equal(Chave, invoice.ChaveNFe);
    }

    [Fact]
    public async Task SameNumberWithDifferentSeries_Succeeds()
    {
        // Chave em branco no documento já gravado é exatamente o estado que barrava o próximo.
        await SeedInvoiceAsync("000000001",
            taxDocumentNumber: "123456", taxDocumentSeries: "1", chaveNFe: "");
        var target = await SeedInvoiceAsync("000000002");

        await Service().ExecuteAsync(target.Key, "123456", "2", "", "joao");

        Assert.Equal("123456", target.TaxDocumentNumber);
        Assert.Equal("2", target.TaxDocumentSeries);
    }

    [Fact]
    public async Task SameNumberAndSeriesInSameBranch_Throws()
    {
        await SeedInvoiceAsync("000000001", taxDocumentNumber: "123456", taxDocumentSeries: "1");
        var target = await SeedInvoiceAsync("000000002");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(target.Key, "123456", "1", "", "joao"));

        Assert.Contains("000000001", ex.Message);
    }

    [Fact]
    public async Task SameNumberAndSeriesInDifferentBranch_Succeeds()
    {
        await SeedInvoiceAsync("000000001", branchCode: "F001",
            taxDocumentNumber: "123456", taxDocumentSeries: "1");
        var target = await SeedInvoiceAsync("000000002", branchCode: "F002");

        await Service().ExecuteAsync(target.Key, "123456", "1", "", "joao");

        Assert.Equal("123456", target.TaxDocumentNumber);
    }

    /// <summary>
    /// Queixa 2: a chave de acesso é opcional. Um documento já gravado sem chave não pode
    /// bloquear todos os seguintes que também não informem chave.
    /// </summary>
    [Fact]
    public async Task BlankAccessKeyOnMultipleDocuments_Succeeds()
    {
        var first = await SeedInvoiceAsync("000000001");
        var second = await SeedInvoiceAsync("000000002");

        await Service().ExecuteAsync(first.Key, "123456", "1", "", "joao");
        await Service().ExecuteAsync(second.Key, "123457", "1", "", "joao");

        Assert.Equal("123457", second.TaxDocumentNumber);
    }

    [Fact]
    public async Task DuplicateAccessKey_ThrowsEvenAcrossBranches()
    {
        await SeedInvoiceAsync("000000001", branchCode: "F001", chaveNFe: Chave);
        var target = await SeedInvoiceAsync("000000002", branchCode: "F002");

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(target.Key, "123456", "1", Chave, "joao"));

        Assert.Contains("000000001", ex.Message);
    }

    /// <summary>
    /// Requisito do cancelamento: o documento cancelado preserva os dados fiscais, mas deixa de
    /// bloquear número, série e chave para outro documento.
    /// </summary>
    [Fact]
    public async Task CancelledDocument_ReleasesNumberSeriesAndAccessKey()
    {
        var cancelled = await SeedInvoiceAsync("000000001", status: InvoiceStatus.Cancelled,
            taxDocumentNumber: "123456", taxDocumentSeries: "1", chaveNFe: Chave);
        var target = await SeedInvoiceAsync("000000002");

        await Service().ExecuteAsync(target.Key, "123456", "1", Chave, "joao");

        Assert.Equal("123456", target.TaxDocumentNumber);
        Assert.Equal(Chave, target.ChaveNFe);
        Assert.Equal(Chave, cancelled.ChaveNFe);
    }

    /// <summary>
    /// A chave chega como <c>null</c> quando o campo do diálogo nunca foi tocado — o parâmetro
    /// OData <c>ChaveNFe</c> é nullable. Não pode casar com as linhas que têm ChaveNFe nula.
    /// </summary>
    [Fact]
    public async Task NullAccessKey_DoesNotCollideWithNullRows()
    {
        await SeedInvoiceAsync("000000001", chaveNFe: null);
        var target = await SeedInvoiceAsync("000000002");

        await Service().ExecuteAsync(target.Key, "123456", "1", null, "joao");

        Assert.Equal("123456", target.TaxDocumentNumber);
        Assert.Null(target.ChaveNFe);
    }

    [Fact]
    public async Task BlankAccessKey_IsPersistedAsNull()
    {
        var invoice = await SeedInvoiceAsync();

        await Service().ExecuteAsync(invoice.Key, "123456", "1", "   ", "joao");

        Assert.Null(invoice.ChaveNFe);
    }

    [Fact]
    public async Task SurroundingWhitespace_IsTrimmedBeforePersisting()
    {
        var invoice = await SeedInvoiceAsync();

        await Service().ExecuteAsync(invoice.Key, " 123456 ", " 1 ", $" {Chave} ", "joao");

        Assert.Equal("123456", invoice.TaxDocumentNumber);
        Assert.Equal("1", invoice.TaxDocumentSeries);
        Assert.Equal(Chave, invoice.ChaveNFe);
    }
}
