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
            CardCode = "C1", SapDocEntry = 10, SapDocNum = 20, SapCancelPending = true,
            SapCancelPendingReason = "x", SapCancelDetectedAt = DateTime.UtcNow,
        };

        SalesInvoiceNfeLock.ResetIssuanceFields(invoice);

        Assert.Null(invoice.SapDocEntry);
        Assert.Null(invoice.SapDocNum);
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
            SapDocEntry = 10, SapDocNum = 20, SapCancelPending = true, SapCancelPendingReason = "motivo",
            SapCancelDetectedAt = detected,
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();

        invoice.SapDocEntry = 99;
        invoice.SapDocNum = 98;
        invoice.SapCancelPending = false;
        invoice.SapCancelPendingReason = "outro";
        invoice.SapCancelDetectedAt = null;
        SalesInvoiceNfeLock.RestoreIssuanceFields(db.Context.Entry(invoice));

        Assert.Equal(10, invoice.SapDocEntry);
        Assert.Equal(20, invoice.SapDocNum);
        Assert.True(invoice.SapCancelPending);
        Assert.Equal("motivo", invoice.SapCancelPendingReason);
        Assert.Equal(detected, invoice.SapCancelDetectedAt);
        Assert.True(invoice.IsSapLinked);
    }
}
