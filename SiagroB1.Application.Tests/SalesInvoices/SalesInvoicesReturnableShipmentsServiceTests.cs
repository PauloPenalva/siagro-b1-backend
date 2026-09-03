using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Romaneios de um documento de saída LEGADO que ainda podem ser devolvidos — a fonte da grade
/// do diálogo de retorno.
/// </summary>
/// <remarks>
/// O diálogo tem de oferecer exatamente o que o serviço de retorno vai aceitar. Oferecer um
/// romaneio já devolvido só produziria erro na confirmação, depois de o usuário escolher.
/// </remarks>
public class SalesInvoicesReturnableShipmentsServiceTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private SalesInvoicesReturnableShipmentsService Service() => new(_db);

    private async Task<SalesInvoice> SeedInvoiceAsync()
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = "C0001",
            BranchCode = "01",
            InvoiceNumber = "000001",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
        };

        _db.Context.SalesInvoices.Add(invoice);
        await _db.SaveChangesAsync();

        return invoice;
    }

    private async Task<StorageTransaction> SeedShipmentAsync(
        SalesInvoice invoice,
        string code,
        decimal netWeight = 30_000m,
        StorageTransactionsStatus status = StorageTransactionsStatus.Invoiced,
        StorageTransactionType type = StorageTransactionType.SalesShipment)
    {
        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "C0001",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            WarehouseName = "ARMAZEM CEAGESP",
            BranchCode = "01",
            TruckCode = "ABC1D23",
            TransactionDate = new DateTime(2026, 9, 1),
            GrossWeight = netWeight,
            NetWeight = netWeight,
            TransactionType = type,
            TransactionStatus = status,
            SalesInvoiceKey = invoice.Key,
        };

        _db.Context.StorageTransactions.Add(shipment);
        await _db.SaveChangesAsync();

        return shipment;
    }

    [Fact]
    public async Task It_lists_the_invoiced_shipments_of_the_document()
    {
        var invoice = await SeedInvoiceAsync();
        await SeedShipmentAsync(invoice, "R1");
        await SeedShipmentAsync(invoice, "R2");

        var shipments = await Service().ExecuteAsync(invoice.Key);

        Assert.Equal(2, shipments.Count);
        Assert.Contains(shipments, s => s.Code == "R1");
        Assert.Contains(shipments, s => s.Code == "R2");
    }

    /// <summary>
    /// O romaneio já devolvido sai da lista: ele é o que sobra de um retorno PARCIAL anterior, e
    /// oferecê-lo de novo devolveria o mesmo volume duas vezes.
    /// </summary>
    [Fact]
    public async Task An_already_returned_shipment_is_left_out()
    {
        var invoice = await SeedInvoiceAsync();
        await SeedShipmentAsync(invoice, "R1");
        await SeedShipmentAsync(invoice, "R2", status: StorageTransactionsStatus.Returned);

        var shipments = await Service().ExecuteAsync(invoice.Key);

        Assert.Equal("R1", Assert.Single(shipments).Code);
    }

    /// <summary>
    /// Romaneio de outro documento não entra, mesmo com o mesmo cliente e produto — é a
    /// armadilha que a consulta de "órfãos" do estorno legado já cometeu uma vez.
    /// </summary>
    [Fact]
    public async Task A_shipment_of_another_document_is_left_out()
    {
        var invoice = await SeedInvoiceAsync();
        var other = await SeedInvoiceAsync();

        await SeedShipmentAsync(invoice, "R1");
        await SeedShipmentAsync(other, "R2");

        var shipments = await Service().ExecuteAsync(invoice.Key);

        Assert.Equal("R1", Assert.Single(shipments).Code);
    }

    /// <summary>
    /// A grade mostra o armazém de cada romaneio: é dele que o grão saiu, e é a informação que o
    /// operador usa para decidir o destino da devolução.
    /// </summary>
    [Fact]
    public async Task It_carries_the_warehouse_and_the_net_weight_of_each_shipment()
    {
        var invoice = await SeedInvoiceAsync();
        await SeedShipmentAsync(invoice, "R1", netWeight: 27_540m);

        var shipment = Assert.Single(await Service().ExecuteAsync(invoice.Key));

        Assert.Equal("ARM01", shipment.WarehouseCode);
        Assert.Equal("ARMAZEM CEAGESP", shipment.WarehouseName);
        Assert.Equal(27_540m, shipment.NetWeight);
        Assert.Equal("ABC1D23", shipment.TruckCode);
    }
}
