using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// A devolução vinculada à NF de devolução do SAP (A/R Credit Memo, GAC-1226) tem as mesmas travas do documento
/// vinculado à invoice de venda: os campos do vínculo são do servidor e a nota não muda pela API.
/// </summary>
public class SalesInvoiceSapCreditMemoLockTests
{
    private static SalesInvoice NewLinkedReturn() => new()
    {
        Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "3", InvoiceType = SalesInvoiceType.Return,
        InvoiceStatus = InvoiceStatus.Confirmed, InvoiceDate = new DateTime(2026, 11, 10),
        SapCreditMemoDocEntry = 10, SapCreditMemoDocNum = 20, TaxDocumentKind = TaxDocumentKind.Nfe,
        TaxDocumentNumber = "000000123", TaxDocumentSeries = "001", ChaveNFe = "K1",
    };

    private static async Task<(UnitOfWork db, SalesInvoice invoice)> SeedLinkedReturnAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = NewLinkedReturn();
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice);
    }

    [Fact]
    public void Credit_memo_link_has_its_own_filtered_unique_index()
    {
        // DocEntry de OINV e de ORIN são sequências independentes: o índice de SapDocEntry não serve.
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none").Options);

        var index = context.Model.FindEntityType(typeof(SalesInvoice))!.GetIndexes()
            .Single(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(SalesInvoice.SapCreditMemoDocEntry));

        Assert.True(index.IsUnique);
        Assert.Equal(
            $"[SapCreditMemoDocEntry] IS NOT NULL AND [InvoiceStatus] <> {(int)InvoiceStatus.Cancelled} AND [InvoiceStatus] <> {(int)InvoiceStatus.Returned}",
            index.GetFilter());
    }

    [Fact]
    public void Linked_return_counts_as_sap_linked()
    {
        Assert.True(NewLinkedReturn().IsSapLinked);
        Assert.False(new SalesInvoice { CardCode = "C1" }.IsSapLinked);
    }

    [Fact]
    public void Creation_never_keeps_credit_memo_link_fields_from_the_body()
    {
        var invoice = NewLinkedReturn();

        SalesInvoiceNfeLock.ResetIssuanceFields(invoice);

        Assert.Null(invoice.SapCreditMemoDocEntry);
        Assert.Null(invoice.SapCreditMemoDocNum);
        Assert.False(invoice.IsSapLinked);
    }

    [Fact]
    public async Task Patch_cannot_change_the_credit_memo_link_fields()
    {
        var (db, invoice) = await SeedLinkedReturnAsync();

        invoice.SapCreditMemoDocEntry = null;
        invoice.SapCreditMemoDocNum = 98;
        SalesInvoiceNfeLock.RestoreIssuanceFields(db.Context.Entry(invoice));

        Assert.Equal(10, invoice.SapCreditMemoDocEntry);
        Assert.Equal(20, invoice.SapCreditMemoDocNum);
    }

    [Fact]
    public async Task Patch_cannot_change_number_series_or_key_of_a_linked_return()
    {
        var (db, invoice) = await SeedLinkedReturnAsync();

        invoice.TaxDocumentNumber = "999";
        invoice.TaxDocumentSeries = "9";
        invoice.ChaveNFe = "OUTRA";
        invoice.WithoutTaxDocument = true;
        SalesInvoiceNfeLock.RestoreIssuanceFields(db.Context.Entry(invoice));

        Assert.Equal("000000123", invoice.TaxDocumentNumber);
        Assert.Equal("001", invoice.TaxDocumentSeries);
        Assert.Equal("K1", invoice.ChaveNFe);
        Assert.False(invoice.WithoutTaxDocument);
    }

    [Fact]
    public async Task Header_of_a_linked_return_cannot_change_customer_date_or_branch()
    {
        var (db, invoice) = await SeedLinkedReturnAsync();

        invoice.CardCode = "C2";

        var error = Assert.Throws<DefaultException>(
            () => SalesInvoiceNfeLock.EnsureHeaderEditable(db.Context.Entry(invoice)));
        Assert.Equal(SalesInvoiceNfeLock.SapLinkedHeaderMessage, error.Message);
    }

    [Fact]
    public void Manual_tax_document_is_refused_on_a_linked_return()
    {
        var error = Assert.Throws<DefaultException>(
            () => SalesInvoiceNfeLock.EnsureManualTaxDocument(NewLinkedReturn()));
        Assert.Equal(SalesInvoiceNfeLock.SapLinkedTaxDocumentMessage, error.Message);
    }

    [Fact]
    public async Task Reverse_confirmation_is_refused_while_the_credit_memo_is_linked()
    {
        // O estorno desfaz o físico da devolução enquanto a nota segue válida no SAP: primeiro desvincula.
        var (db, invoice) = await SeedLinkedReturnAsync();
        var hook = new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context));
        var service = new SalesInvoicesReverseConfirmService(db,
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            hook,
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

        var error = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(SalesInvoicesReverseConfirmService.SapCreditMemoLinkedMessage, error.Message);
        Assert.Equal(InvoiceStatus.Confirmed,
            (await db.Context.SalesInvoices.AsNoTracking().SingleAsync()).InvoiceStatus);
    }
}
