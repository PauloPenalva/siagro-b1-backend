using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// O discriminador legado × novo. A ordem das cláusulas é o ponto: um documento de carga tem
/// <c>SalesTransactions</c> VAZIA, então perguntar a coleção primeiro — que é o que o código
/// fazia antes da Carga existir — manda o documento de carga para o ramo AVULSO.
/// </summary>
public class SalesInvoiceOriginResolverTests
{
    private static SalesInvoice Invoice(Guid? loadKey, int shipmentCount)
    {
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C001", ShipmentLoadKey = loadKey };

        for (var i = 0; i < shipmentCount; i++)
        {
            invoice.SalesTransactions.Add(new StorageTransaction
            {
                Key = Guid.NewGuid(),
                CardCode = "C001",
                ItemCode = "SOJA",
                UnitOfMeasureCode = "KG",
                WarehouseCode = "ARM01",
            });
        }

        return invoice;
    }

    [Fact]
    public void A_load_invoice_resolves_to_ShipmentLoad_even_with_no_shipments()
    {
        // Este é o caso que a contagem sozinha errava.
        var invoice = Invoice(Guid.NewGuid(), shipmentCount: 0);

        Assert.Equal(SalesInvoiceOrigin.ShipmentLoad, SalesInvoiceOriginResolver.Resolve(invoice));
        Assert.True(SalesInvoiceOriginResolver.ConsumesShipments(invoice));
        Assert.True(SalesInvoiceOriginResolver.IsShipmentLoad(invoice));
    }

    [Fact]
    public void The_load_key_wins_over_the_shipment_collection()
    {
        var invoice = Invoice(Guid.NewGuid(), shipmentCount: 2);

        Assert.Equal(SalesInvoiceOrigin.ShipmentLoad, SalesInvoiceOriginResolver.Resolve(invoice));
    }

    [Fact]
    public void A_legacy_invoice_still_resolves_to_LegacyShipment()
    {
        var invoice = Invoice(loadKey: null, shipmentCount: 2);

        Assert.Equal(SalesInvoiceOrigin.LegacyShipment, SalesInvoiceOriginResolver.Resolve(invoice));
        Assert.True(SalesInvoiceOriginResolver.ConsumesShipments(invoice));
        Assert.False(SalesInvoiceOriginResolver.IsShipmentLoad(invoice));
    }

    [Fact]
    public void A_standalone_invoice_resolves_to_Standalone()
    {
        var invoice = Invoice(loadKey: null, shipmentCount: 0);

        Assert.Equal(SalesInvoiceOrigin.Standalone, SalesInvoiceOriginResolver.Resolve(invoice));
        Assert.False(SalesInvoiceOriginResolver.ConsumesShipments(invoice));
    }
}
