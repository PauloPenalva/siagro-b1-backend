using Microsoft.Extensions.Logging.Abstractions;
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
/// GAC-1226: a origem devolvida libera a NF do SAP, que pode ir para outro documento. Desfazer a devolução
/// devolveria a origem a Confirmada com a mesma NF — o índice único filtrado recusaria no SQL Server (2601),
/// e o InMemory não acusa. A regra recusa antes de qualquer escrita.
/// </summary>
public class SalesInvoiceReturnOriginSapLinkTests
{
    private const string Expected =
        "A NF do SAP 555 já foi vinculada ao documento SAI-2; cancele-o antes de estornar a devolução.";

    private static async Task<(UnitOfWork db, SalesInvoice origin, SalesInvoice ret)> SeedAsync(
        InvoiceStatus returnStatus, bool otherActive = true)
    {
        var db = TestDb.CreateUnitOfWork();
        var origin = new SalesInvoice
        {
            Key = Guid.NewGuid(), InvoiceNumber = "SAI-1", CardCode = "C1", BranchCode = "01",
            InvoiceStatus = InvoiceStatus.Returned, SapDocEntry = 10, SapDocNum = 77, TaxDocumentNumber = "555",
        };
        var other = new SalesInvoice
        {
            Key = Guid.NewGuid(), InvoiceNumber = "SAI-2", CardCode = "C1", BranchCode = "01",
            InvoiceStatus = otherActive ? InvoiceStatus.Pending : InvoiceStatus.Cancelled, SapDocEntry = 10,
        };
        var ret = new SalesInvoice
        {
            Key = Guid.NewGuid(), InvoiceNumber = "DEV-1", CardCode = "C1", BranchCode = "01",
            InvoiceStatus = returnStatus, InvoiceType = SalesInvoiceType.Return, SalesInvoiceOriginKey = origin.Key,
        };
        db.Context.SalesInvoices.AddRange(origin, other, ret);
        await db.SaveChangesAsync();
        return (db, origin, ret);
    }

    [Fact]
    public async Task Restore_refuses_when_another_active_document_holds_the_origin_sap_invoice()
    {
        var (db, origin, ret) = await SeedAsync(InvoiceStatus.Pending);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesInvoicesReturnOriginRestoreService.ExecuteAsync(db.Context, ret, "tester"));

        Assert.Equal(Expected, ex.Message);
        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);
    }

    [Fact]
    public async Task Restore_message_uses_sap_doc_num_when_the_origin_has_no_tax_document_number()
    {
        var (db, origin, ret) = await SeedAsync(InvoiceStatus.Pending);
        origin.TaxDocumentNumber = null;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesInvoicesReturnOriginRestoreService.ExecuteAsync(db.Context, ret, "tester"));

        Assert.Equal("A NF do SAP 77 já foi vinculada ao documento SAI-2; cancele-o antes de estornar a devolução.",
            ex.Message);
    }

    [Fact]
    public async Task Restore_proceeds_when_the_other_document_is_cancelled()
    {
        var (db, origin, ret) = await SeedAsync(InvoiceStatus.Pending, otherActive: false);

        await SalesInvoicesReturnOriginRestoreService.ExecuteAsync(db.Context, ret, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, origin.InvoiceStatus);
    }

    [Fact]
    public async Task Deleting_the_return_is_refused_and_nothing_is_removed()
    {
        var (db, origin, ret) = await SeedAsync(InvoiceStatus.Pending);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesDeleteService(db,
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesDeleteService>.Instance)
            .ExecuteAsync(ret.Key, "tester"));

        Assert.Equal(Expected, ex.Message);
        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);
    }

    [Fact]
    public async Task Cancelling_the_return_is_refused_before_any_write()
    {
        var (db, origin, ret) = await SeedAsync(InvoiceStatus.Pending);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => new SalesInvoicesCancelService(db,
                new SalesShipmentReleasesRecalculateShippedService(db.Context),
                new SalesContractsAllocationDeleteForInvoiceService(db),
                new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
                NullLogger<SalesInvoicesCancelService>.Instance)
            .ExecuteAsync(ret.Key, "tester"));

        Assert.Equal(Expected, ex.Message);
        Assert.Equal(InvoiceStatus.Pending, ret.InvoiceStatus);
        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);
    }
}
