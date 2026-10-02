using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Travas do documento com NF-e (spec §9.4 + decisão P11 do plano). Em processamento nada muda; com
/// a NF-e autorizada, o que foi ao XML não muda (inclusive depois de estornar a confirmação), e o
/// cancelamento espera o cancelamento na SEFAZ (2b). Os campos da NF-e só a emissão escreve.
/// </summary>
public class SalesInvoiceNfeLockTests
{
    private static async Task<(UnitOfWork Db, SalesInvoice Invoice)> SeedAsync(
        NfeStatus nfe, InvoiceStatus status = InvoiceStatus.Pending)
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", InvoiceStatus = status, NfeStatus = nfe,
            InvoiceDate = new DateTime(2026, 10, 2), Comments = "antes",
            Items =
            [
                new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m },
            ],
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice);
    }

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db) =>
        new(db, new FakeBusinessPartnerService(names: new() { ["C1"] = "CLIENTE", ["C2"] = "OUTRO" }),
            TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesItemsUpdateService ItemUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    [Fact]
    public async Task Processing_document_header_cannot_be_edited()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Processing);
        invoice.Comments = "depois";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Contains("em processamento", ex.Message);
    }

    [Fact]
    public async Task Authorized_document_cannot_change_the_customer()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.CardCode = "C2";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Contains("autorizada", ex.Message);
    }

    [Fact]
    public async Task Authorized_document_can_change_internal_comments()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);
        invoice.Comments = "depois";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal("depois", (await db.Context.SalesInvoices.AsNoTracking().SingleAsync()).Comments);
    }

    [Fact]
    public async Task Patch_cannot_write_the_issuance_fields()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.None);
        invoice.NfeStatus = NfeStatus.Authorized;
        invoice.NfeProtocol = "123";

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        var saved = await db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.None, saved.NfeStatus);
        Assert.Null(saved.NfeProtocol);
    }

    [Fact]
    public void Create_ignores_issuance_fields_in_the_body()
    {
        var invoice = new SalesInvoice
        {
            CardCode = "C1", NfeStatus = NfeStatus.Authorized, NfeProtocol = "1", NfeRandomCode = "12345678",
            NfeConfirmationError = "x",
        };

        SalesInvoiceNfeLock.ResetIssuanceFields(invoice);

        Assert.Equal(NfeStatus.None, invoice.NfeStatus);
        Assert.Null(invoice.NfeProtocol);
        Assert.Null(invoice.NfeRandomCode);
        Assert.Null(invoice.NfeConfirmationError);
    }

    [Fact]
    public async Task Authorized_line_cannot_change_quantity()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.Quantity = 20m;

        await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester"));
    }

    [Fact]
    public async Task Authorized_line_accepts_the_delivery_reconciliation()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.DeliveredQuantity = 9m;

        await ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester");

        Assert.Equal(9m, (await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync()).DeliveredQuantity);
    }

    [Fact]
    public async Task Line_cannot_be_added_to_an_authorized_document()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized);

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsCreateService(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
                    TaxTestServices.InactiveApply(db), NullLogger<SalesInvoicesItemsCreateService>.Instance)
                .ExecuteAsync(new SalesInvoiceItem
                {
                    Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 1m,
                }, "tester"));
    }

    [Fact]
    public async Task Line_cannot_be_removed_while_processing()
    {
        var (db, _) = await SeedAsync(NfeStatus.Processing);
        var item = await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesItemsDeleteService(db, NullLogger<SalesInvoicesItemsDeleteService>.Instance).ExecuteAsync(item.Key!.Value));
    }

    [Theory]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Denied)]
    public async Task Document_with_nfe_cannot_be_deleted(NfeStatus nfe)
    {
        var (db, invoice) = await SeedAsync(nfe);

        await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesDeleteService(db,
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesDeleteService>.Instance)
            .ExecuteAsync(invoice.Key, "tester"));
    }

    [Fact]
    public async Task Rejected_document_can_be_deleted()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, Kind = SalesInvoiceNfeXmlKind.Signed,
            Xml = "<NFe/>", CreatedAt = DateTime.Now,
        });
        await db.SaveChangesAsync();

        var deleted = await new SalesInvoicesDeleteService(db,
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesDeleteService>.Instance)
            .ExecuteAsync(invoice.Key, "tester");

        Assert.True(deleted);
        Assert.False(await db.Context.SalesInvoiceNfeXmls.AnyAsync(x => x.SalesInvoiceKey == invoice.Key));
    }

    [Fact]
    public async Task Document_with_authorized_nfe_cannot_be_cancelled()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Authorized, InvoiceStatus.Confirmed);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesCancelService(db,
                new SalesShipmentReleasesRecalculateShippedService(db.Context),
                new SalesContractsAllocationDeleteForInvoiceService(db),
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesCancelService>.Instance)
            .ExecuteAsync(invoice.Key, "tester"));

        Assert.Contains("SEFAZ", ex.Message);
    }

    [Fact]
    public async Task Manual_tax_document_is_refused_after_issuance()
    {
        var (db, invoice) = await SeedAsync(NfeStatus.Rejected);

        await Assert.ThrowsAsync<DefaultException>(() =>
            new SalesInvoicesSetDocumentNumberService(db, new SalesInvoicesChangeLogService(db.Context))
                .ExecuteAsync(invoice.Key, "000000010", "1", null, "tester"));
    }
}
