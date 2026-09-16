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

    private SalesInvoicesSetDocumentNumberService Service() => new(_db, new SalesInvoicesChangeLogService(_db.Context));

    private async Task<SalesInvoice> SeedInvoiceAsync(
        string invoiceNumber = "000000001",
        string branchCode = "F001",
        InvoiceStatus status = InvoiceStatus.Confirmed,
        string? taxDocumentNumber = null,
        string? taxDocumentSeries = null,
        string? chaveNFe = null,
        bool withoutTaxDocument = false)
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
            WithoutTaxDocument = withoutTaxDocument,
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

    // -----------------------------------------------------------------------------------------
    // GAC-1174: operação sem nota fiscal
    // -----------------------------------------------------------------------------------------

    private List<SalesInvoiceChangeLog> LogsOf(Guid invoiceKey) =>
        _db.Context.SalesInvoicesChangeLogs.Where(l => l.SalesInvoiceKey == invoiceKey).ToList();

    /// <summary>
    /// Marcar como sem nota fiscal e ter nota fiscal se excluem: o flag vence e apaga número,
    /// série e chave, mesmo que cheguem preenchidos.
    /// </summary>
    [Fact]
    public async Task MarkingWithoutTaxDocument_SetsFlagAndClearsTaxDocumentData()
    {
        var invoice = await SeedInvoiceAsync(
            taxDocumentNumber: "123456", taxDocumentSeries: "1", chaveNFe: Chave);

        await Service().ExecuteAsync(invoice.Key, "123456", "1", Chave, "joao", withoutTaxDocument: true);

        Assert.True(invoice.WithoutTaxDocument);
        Assert.Null(invoice.TaxDocumentNumber);
        Assert.Null(invoice.TaxDocumentSeries);
        Assert.Null(invoice.ChaveNFe);
    }

    /// <summary>
    /// Como os dados fiscais são descartados, a trava de duplicidade não pode barrar a marcação
    /// por um número que nem vai ser gravado.
    /// </summary>
    [Fact]
    public async Task MarkingWithoutTaxDocument_IgnoresDuplicateGuard()
    {
        await SeedInvoiceAsync("000000001", taxDocumentNumber: "123456", taxDocumentSeries: "1", chaveNFe: Chave);
        var target = await SeedInvoiceAsync("000000002");

        await Service().ExecuteAsync(target.Key, "123456", "1", Chave, "joao", withoutTaxDocument: true);

        Assert.True(target.WithoutTaxDocument);
    }

    [Fact]
    public async Task InformingTaxDocument_ClearsWithoutTaxDocumentFlag()
    {
        var invoice = await SeedInvoiceAsync(withoutTaxDocument: true);

        await Service().ExecuteAsync(invoice.Key, "123456", "1", Chave, "joao");

        Assert.False(invoice.WithoutTaxDocument);
        Assert.Equal("123456", invoice.TaxDocumentNumber);
    }

    [Fact]
    public async Task ChangingWithoutTaxDocumentFlag_IsLogged()
    {
        var invoice = await SeedInvoiceAsync();

        await Service().ExecuteAsync(invoice.Key, null, null, null, "joao", withoutTaxDocument: true);
        await Service().ExecuteAsync(invoice.Key, null, null, null, "maria", withoutTaxDocument: false);

        var logs = LogsOf(invoice.Key).OrderBy(l => l.ChangedAt).ThenBy(l => l.ChangedBy).ToList();
        Assert.Equal(2, logs.Count);

        var marked = logs.Single(l => l.ChangedBy == "joao");
        Assert.Equal(ContractChangeLogFields.WithoutTaxDocument, marked.Field);
        Assert.Equal("Não", marked.OldValue);
        Assert.Equal("Sim", marked.NewValue);

        var unmarked = logs.Single(l => l.ChangedBy == "maria");
        Assert.Equal("Sim", unmarked.OldValue);
        Assert.Equal("Não", unmarked.NewValue);
    }

    /// <summary>
    /// Informar ou corrigir a NF sem mexer no flag não é mudança de "sem nota fiscal" — o log
    /// continua sem linha, como antes desta feature.
    /// </summary>
    [Fact]
    public async Task InformingTaxDocumentWithoutChangingFlag_DoesNotLog()
    {
        var invoice = await SeedInvoiceAsync();

        await Service().ExecuteAsync(invoice.Key, "123456", "1", Chave, "joao");

        Assert.Empty(LogsOf(invoice.Key));
    }
}
