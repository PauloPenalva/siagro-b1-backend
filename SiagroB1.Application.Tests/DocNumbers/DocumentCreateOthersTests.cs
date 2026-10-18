using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.OwnershipTransfers;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.WeighingTickets;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Application.Tests.WarehouseReconciliations;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.DocNumbers;

/// <summary>
/// Os serviços de criação de documentos numerados entregam à numeração a série escolhida, o código
/// digitado, o limite da coluna e o predicado de duplicidade da própria tabela.
/// </summary>
public class DocumentCreateOthersTests
{
    private static readonly Guid SeriesKey = Guid.NewGuid();

    [Fact]
    public async Task SalesInvoicesCreate_PassesTypedInvoiceNumber()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "SP" });
        db.Context.SalesInvoices.Add(new SalesInvoice { CardCode = "C1", InvoiceNumber = "USED" });
        await db.SaveChangesAsync();
        await new UsageService(db, NullLogger<UsageService>.Instance).CreateAsync(new UsageModel
        {
            Name = "Venda", CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            RequiresQuantity = true, IsDefault = true,
        });

        var partners = new FakeBusinessPartnerService(names: new() { ["C1"] = "CLIENTE" }, states: new() { ["C1"] = "SP" });
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var fake = new FakeDocNumberSequenceService();
        var service = new SalesInvoicesCreateService(
            db, partners,
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
            fake,
            new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.InactiveApply(db),
            TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance);

        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "C1", InvoiceDate = new DateTime(2026, 10, 2),
            DocNumberKey = SeriesKey, InvoiceNumber = "NF-123",
            Items =
            [
                new SalesInvoiceItem
                {
                    Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
                    SalesContractKey = Guid.NewGuid(),
                },
            ],
        };
        await service.ExecuteAsync(invoice, "tester");

        await DocNumberRequestAsserts.AssertRequestAsync(
            fake, TransactionCode.SalesInvoice, SeriesKey, "NF-123", 9, "USED");
        Assert.Equal("NF-123", invoice.InvoiceNumber);
        Assert.Equal(SeriesKey, invoice.DocNumberKey);
    }

    [Fact]
    public async Task WeighingTicketsCreate_UsesChosenSeries()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.WeighingTickets.Add(NewTicket("USED"));
        await db.SaveChangesAsync();
        var fake = new FakeDocNumberSequenceService();
        var service = new WeighingTicketsCreateService(
            db, new FakeBusinessPartnerService(), new FakeItemService(), fake,
            NullLogger<WeighingTicketsCreateService>.Instance);

        var ticket = NewTicket("T-MANUAL");
        ticket.DocNumberKey = SeriesKey;
        await service.ExecuteAsync(ticket, "tester");

        await DocNumberRequestAsserts.AssertRequestAsync(
            fake, TransactionCode.WeighingTicket, SeriesKey, "T-MANUAL", 15, "USED");
        Assert.Equal("T-MANUAL", ticket.Code);
    }

    private static WeighingTicket NewTicket(string code) => new()
    {
        Code = code, Type = WeighingTicketType.Receipt, ItemCode = "SOJA", CardCode = "F0001",
        TruckCode = "ABC1D23", TruckDriverCode = "M001",
    };

    [Fact]
    public async Task StorageAddressesCreate_UsesChosenSeries()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.StorageAddresses.Add(NewAddress("USED"));
        await db.SaveChangesAsync();
        var fake = new FakeDocNumberSequenceService();
        var service = new StorageAddressesCreateService(
            db, fake, new FakeBusinessPartnerService(), new FakeItemService(), new FakeWarehouseService(),
            new WarehouseComplementService(db), new TestLogger<StorageAddressesCreateService>());

        var address = NewAddress("L-MANUAL");
        address.DocNumberKey = SeriesKey;
        await service.ExecuteAsync(address, "tester");

        await DocNumberRequestAsserts.AssertRequestAsync(
            fake, TransactionCode.StorageAddress, SeriesKey, "L-MANUAL", 50, "USED");
        Assert.Equal("L-MANUAL", address.Code);
    }

    private static StorageAddress NewAddress(string code) => new()
    {
        Code = code, Description = "Lote", CardCode = "C0001", ItemCode = "SOJA", WarehouseCode = "ARM01", UoM = "KG",
    };

    [Fact]
    public async Task OwnershipTransfersCreate_UsesChosenSeries()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.OwnershipTransfers.Add(NewTransfer("USED"));
        await db.SaveChangesAsync();
        var fake = new FakeDocNumberSequenceService();
        var service = new OwnershipTransfersCreateService(
            db, new FakeItemService(),
            new OwnershipTransfersValidateContractService(db, new WarehouseComplementService(db), new FakeStringLocalizer<Resource>()),
            fake, NullLogger<OwnershipTransfersCreateService>.Instance);

        var transfer = NewTransfer("TT-MANUAL");
        transfer.DocNumberKey = SeriesKey;
        await service.ExecuteAsync(transfer, "tester");

        await DocNumberRequestAsserts.AssertRequestAsync(
            fake, TransactionCode.OwnershipTransfer, SeriesKey, "TT-MANUAL", 50, "USED");
        Assert.Equal("TT-MANUAL", transfer.TransferCode);
    }

    private static OwnershipTransfer NewTransfer(string code) => new()
    {
        TransferCode = code, ItemCode = "SOJA", StorageAddressOriginCode = "L1", StorageAddressDestinationCode = "L2",
        UomCode = "KG",
    };

    [Fact]
    public async Task WarehouseReconciliationsCreate_UsesChosenSeries()
    {
        var ctx = new WarehouseReconciliationsTestContext();
        var reason = await ctx.SeedReasonAsync();
        var used = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m, DateTime.Today.AddDays(-30));
        used.Code = "USED";
        used.Status = WarehouseReconciliationStatus.Cancelled;
        ctx.Db.Context.WarehouseReconciliations.Add(used);
        await ctx.Db.SaveChangesAsync();

        var fake = new FakeDocNumberSequenceService();
        var service = new Application.Services.WarehouseReconciliations.WarehouseReconciliationsCreateService(
            ctx.Db, fake, ctx.Descriptions(), ctx.Guard(), ctx.Resource);

        var r = WarehouseReconciliationsTestContext.NewReconciliation(reason.Key, 10m);
        r.DocNumberKey = SeriesKey;
        r.Code = "CS-MANUAL";
        await service.ExecuteAsync(r, "tester");

        await DocNumberRequestAsserts.AssertRequestAsync(
            fake, TransactionCode.WarehouseReconciliation, SeriesKey, "CS-MANUAL", 50, "USED");
        Assert.Equal("CS-MANUAL", r.Code);
    }

    [Fact]
    public async Task ShipmentLoadsCreate_UsesChosenSeries()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.ShipmentLoads.Add(NewLoad("USED"));
        await db.SaveChangesAsync();
        var fake = new FakeDocNumberSequenceService();
        var service = new ShipmentLoadsCreateService(db, fake, new ShipmentLoadsMovementLogService(db.Context));

        var load = NewLoad("CG-MANUAL");
        load.DocNumberKey = SeriesKey;
        var created = await service.ExecuteAsync(load, "tester");

        await DocNumberRequestAsserts.AssertRequestAsync(
            fake, TransactionCode.ShipmentLoad, SeriesKey, "CG-MANUAL", 50, "USED");
        Assert.Equal("CG-MANUAL", created.Code);
        Assert.Equal(SeriesKey, created.DocNumberKey);
    }

    private static ShipmentLoad NewLoad(string code) => new()
    {
        Code = code, BranchCode = "01", TruckCode = "ABC1D23", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
        WarehouseCode = "ARM01",
    };
}
