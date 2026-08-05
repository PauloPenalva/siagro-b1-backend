using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.CustomerReturns;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.CustomerReturns;

/// <summary>
/// Conciliação da devolução do cliente contra a quebra apurada na entrega.
///
/// A invariante que sustenta o desenho: a devolução NÃO move saldo de contrato. O volume já
/// foi devolvido pelo fator efetivo quando a entrega foi conferida — mexer aqui creditaria o
/// contrato duas vezes.
/// </summary>
public class CustomerReturnsReconciliationTests
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
    public void Assessed_shortage_is_billed_minus_net_quantity()
    {
        var item = new SalesInvoiceItem
        {
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
            DeliveredQuantity = 980m,
            QuantityLoss = 5m,
            DeliveryStatus = SalesInvoiceDeliveryStatus.Closed,
        };

        // 1000 faturado − (980 entregue − 5 de desconto) = 25
        Assert.Equal(25m, item.AssessedShortage);
    }

    [Fact]
    public void Assessed_shortage_is_zero_while_the_delivery_is_open()
    {
        var item = new SalesInvoiceItem
        {
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
            DeliveredQuantity = 980m,
            DeliveryStatus = SalesInvoiceDeliveryStatus.Open,
        };

        // Sem conferência não há quebra apurada: o fator efetivo ainda vale 1.
        Assert.Equal(0m, item.AssessedShortage);
    }

    [Fact]
    public void Difference_is_zero_when_the_return_matches_the_shortage()
    {
        var origin = new SalesInvoiceItem
        {
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
            DeliveredQuantity = 980m,
            DeliveryStatus = SalesInvoiceDeliveryStatus.Closed,
        };

        var line = new CustomerReturnItem { Quantity = 20m, SalesInvoiceItem = origin };

        Assert.Equal(20m, line.AssessedShortage);
        Assert.Equal(0m, line.Difference);
    }

    [Fact]
    public void Difference_is_reported_when_the_return_does_not_match()
    {
        var origin = new SalesInvoiceItem
        {
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = 1000m,
            DeliveredQuantity = 980m,
            DeliveryStatus = SalesInvoiceDeliveryStatus.Closed,
        };

        var line = new CustomerReturnItem { Quantity = 15m, SalesInvoiceItem = origin };

        // Devolveu menos do que a quebra: sobra 5 sem cobertura fiscal.
        Assert.Equal(-5m, line.Difference);
    }

    [Fact]
    public async Task Eligible_origins_only_bring_closed_deliveries_with_shortage()
    {
        var db = TestDb.CreateUnitOfWork();
        var contract = SalesContractsAllocationTestSupport.NewContract(10_000m);
        db.Context.SalesContracts.Add(contract);

        var withShortage = ClosedItem(db, contract, quantity: 1000m, delivered: 980m);
        ClosedItem(db, contract, quantity: 1000m, delivered: 1000m);          // sem quebra
        ClosedItem(db, contract, quantity: 500m, delivered: 400m,
            status: InvoiceStatus.Cancelled);                                  // cancelado
        ClosedItem(db, contract, quantity: 700m, delivered: 600m,
            cardCode: "C0002");                                                // outro cliente

        var openDelivery = SalesContractsAllocationTestSupport.NewInvoice();
        var openItem = SalesContractsAllocationTestSupport.NewItem(
            openDelivery, contract.Key, null, 900m);
        openItem.DeliveredQuantity = 800m;
        openItem.DeliveryStatus = SalesInvoiceDeliveryStatus.Open;             // entrega aberta
        db.Context.SalesInvoices.Add(openDelivery);

        await db.SaveChangesAsync();

        var origins = await new CustomerReturnsGetOriginItemsService(db)
            .QueryByCardCode("C0001")
            .ToListAsync();

        var only = Assert.Single(origins);
        Assert.Equal(withShortage.Key, only.SalesInvoiceItemKey);
        Assert.Equal(20m, only.AssessedShortage);
    }
}
