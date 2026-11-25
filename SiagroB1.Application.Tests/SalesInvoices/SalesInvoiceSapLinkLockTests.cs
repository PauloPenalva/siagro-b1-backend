using SiagroB1.Domain.Exceptions;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>Os campos do vínculo com a invoice do SAP (GAC-1226) são do servidor: nunca vêm do corpo.</summary>
public class SalesInvoiceSapLinkLockTests
{
    [Fact]
    public void Creation_never_keeps_sap_link_fields_from_the_body()
    {
        var invoice = new SalesInvoice
        {
            CardCode = "C1", SapDocEntry = 10, SapDocNum = 20, SapLinkType = SapLinkType.ValueDivergent, SapCancelPending = true,
            SapCancelPendingReason = "x", SapCancelDetectedAt = DateTime.UtcNow,
        };

        SalesInvoiceNfeLock.ResetIssuanceFields(invoice);

        Assert.Null(invoice.SapDocEntry);
        Assert.Null(invoice.SapDocNum);
        Assert.Null(invoice.SapLinkType);
        Assert.False(invoice.SapCancelPending);
        Assert.Null(invoice.SapCancelPendingReason);
        Assert.Null(invoice.SapCancelDetectedAt);
        Assert.False(invoice.IsSapLinked);
    }

    [Fact]
    public async Task Patch_cannot_change_the_sap_link_fields()
    {
        var db = TestDb.CreateUnitOfWork();
        var detected = new DateTime(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", InvoiceStatus = InvoiceStatus.Pending,
            SapDocEntry = 10, SapDocNum = 20, SapLinkType = SapLinkType.QuantityAndValueDivergent,
            SapCancelPending = true, SapCancelPendingReason = "motivo",
            SapCancelDetectedAt = detected,
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.SapDocEntry = 99;
        invoice.SapDocNum = 98;
        invoice.SapLinkType = SapLinkType.Normal;
        invoice.SapCancelPending = false;
        invoice.SapCancelPendingReason = "outro";
        invoice.SapCancelDetectedAt = null;
        SalesInvoiceNfeLock.RestoreIssuanceFields(db.Context.Entry(invoice));

        Assert.Equal(10, invoice.SapDocEntry);
        Assert.Equal(20, invoice.SapDocNum);
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, invoice.SapLinkType);
        Assert.True(invoice.SapCancelPending);
        Assert.Equal("motivo", invoice.SapCancelPendingReason);
        Assert.Equal(detected, invoice.SapCancelDetectedAt);
        Assert.True(invoice.IsSapLinked);
    }

    private static async Task<(SiagroB1.Infra.UnitOfWork db, SalesInvoice invoice)> SeedLinkedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", InvoiceStatus = InvoiceStatus.Pending,
            InvoiceDate = new DateTime(2026, 11, 10), SapDocEntry = 10, SapDocNum = 20,
            TaxDocumentKind = TaxDocumentKind.Nfe, TaxDocumentNumber = "123", TaxDocumentSeries = "1",
            ChaveNFe = "K1", WithoutTaxDocument = false,
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice);
    }

    [Fact]
    public async Task Patch_cannot_change_number_series_key_kind_or_without_tax_document_of_a_linked_document()
    {
        var (h, invoice) = await SeedLinkedAsync();

        invoice.TaxDocumentNumber = "999";
        invoice.TaxDocumentSeries = "9";
        invoice.ChaveNFe = "OUTRA";
        invoice.TaxDocumentKind = TaxDocumentKind.Other;
        invoice.WithoutTaxDocument = true;
        SalesInvoiceNfeLock.RestoreIssuanceFields(h.Context.Entry(invoice));

        Assert.Equal("123", invoice.TaxDocumentNumber);
        Assert.Equal("1", invoice.TaxDocumentSeries);
        Assert.Equal("K1", invoice.ChaveNFe);
        Assert.Equal(TaxDocumentKind.Nfe, invoice.TaxDocumentKind);
        Assert.False(invoice.WithoutTaxDocument);
    }

    [Theory]
    [InlineData("CardCode")]
    [InlineData("InvoiceDate")]
    [InlineData("BranchCode")]
    public async Task Patch_refuses_customer_date_or_branch_change_on_a_linked_document(string field)
    {
        var (h, invoice) = await SeedLinkedAsync();
        switch (field)
        {
            case "CardCode": invoice.CardCode = "C2"; break;
            case "InvoiceDate": invoice.InvoiceDate = new DateTime(2026, 11, 11); break;
            default: invoice.BranchCode = "02"; break;
        }

        var ex = Assert.Throws<DefaultException>(() =>
            SalesInvoiceNfeLock.EnsureHeaderEditable(h.Context.Entry(invoice)));
        Assert.Equal(SalesInvoiceNfeLock.SapLinkedHeaderMessage, ex.Message);
    }

    [Fact]
    public async Task Patch_keeps_non_fiscal_fields_editable_on_a_linked_document()
    {
        var (h, invoice) = await SeedLinkedAsync();
        invoice.Comments = "novo comentário";

        SalesInvoiceNfeLock.EnsureHeaderEditable(h.Context.Entry(invoice));
        SalesInvoiceNfeLock.RestoreIssuanceFields(h.Context.Entry(invoice));

        Assert.Equal("novo comentário", invoice.Comments);
    }
}
