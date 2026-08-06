using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Leitura do documento de entrada e das origens elegíveis para amarrar uma devolução.
///
/// Migrado de <c>CustomerReturnsReconciliationTests</c> sem mudança de regra: a rotina nova
/// absorve a devolução, não a reinventa.
/// </summary>
public class PurchaseInvoicesOriginItemsTests
{
    private static SalesInvoiceItem ClosedItem(
        UnitOfWork db, SalesContract contract,
        decimal quantity = 1000m, decimal delivered = 980m, decimal loss = 0m,
        InvoiceStatus status = InvoiceStatus.Confirmed, string cardCode = "C0001")
    {
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(status, cardCode: cardCode);
        var item = SalesContractsAllocationTestSupport.NewItem(
            invoice, contract.Key, releaseKey: null, quantity);

        item.DeliveredQuantity = delivered;
        item.QuantityLoss = loss;
        item.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;

        db.Context.SalesInvoices.Add(invoice);

        return item;
    }

    [Fact]
    public async Task Eligible_origins_only_bring_closed_deliveries_with_shortage()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContractsAllocationTestSupport.NewContract(10_000m);
        db.Context.SalesContracts.Add(contract);

        var withShortage = ClosedItem(db, contract, quantity: 1000m, delivered: 980m);
        ClosedItem(db, contract, quantity: 1000m, delivered: 1000m);           // sem quebra
        ClosedItem(db, contract, quantity: 500m, delivered: 400m,
            status: InvoiceStatus.Cancelled);                                   // cancelado
        ClosedItem(db, contract, quantity: 700m, delivered: 600m,
            cardCode: "C0002");                                                 // outro cliente

        var openDelivery = SalesContractsAllocationTestSupport.NewInvoice();
        var openItem = SalesContractsAllocationTestSupport.NewItem(
            openDelivery, contract.Key, null, 900m);
        openItem.DeliveredQuantity = 800m;
        openItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Open;              // entrega aberta
        db.Context.SalesInvoices.Add(openDelivery);

        await db.SaveChangesAsync();

        var origins = await new PurchaseInvoicesGetOriginItemsService(db)
            .QueryByCardCode("C0001")
            .ToListAsync();

        var only = Assert.Single(origins);
        Assert.Equal(withShortage.Key, only.SalesInvoiceItemKey);
        Assert.Equal(20m, only.AssessedShortage);
    }

    [Fact]
    public async Task Get_by_id_loads_the_origin_so_the_shortage_is_not_silently_zero()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContractsAllocationTestSupport.NewContract(10_000m);
        db.Context.SalesContracts.Add(contract);

        var origin = ClosedItem(db, contract, quantity: 1000m, delivered: 980m);
        await db.SaveChangesAsync();

        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            InvoiceType = PurchaseInvoiceType.Return,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            Quantity = 20m,
            SalesInvoiceItemKey = origin.Key,
        });
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var loaded = await new PurchaseInvoicesGetService(db).GetByIdAsync(invoice.Key);

        // Sem o ThenInclude a navegação vem null, isto voltaria 0 e TODA linha pareceria
        // divergente — a falha silenciosa que o Include existe para evitar.
        Assert.Equal(20m, loaded.Items.Single().AssessedShortage);
        Assert.Equal(0m, loaded.Items.Single().Difference);
    }

    [Fact]
    public async Task Query_all_brings_the_documents_with_their_lines()
    {
        var db = TestDb.CreateUnitOfWork();

        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "F0001" };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "MILHO", Quantity = 5m, UnitPrice = 2m,
        });
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var all = await new PurchaseInvoicesGetService(db).QueryAll().ToListAsync();

        var only = Assert.Single(all);
        Assert.Equal("MILHO", only.Items.Single().ItemCode);
        Assert.Equal(10m, only.TotalInvoiceItems);
    }

    [Fact]
    public async Task Get_by_id_throws_when_the_document_does_not_exist()
    {
        var db = TestDb.CreateUnitOfWork();

        await Assert.ThrowsAsync<Domain.Exceptions.NotFoundException>(
            () => new PurchaseInvoicesGetService(db).GetByIdAsync(Guid.NewGuid()));
    }
}
