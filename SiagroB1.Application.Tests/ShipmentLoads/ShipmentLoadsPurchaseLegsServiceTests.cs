using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// GAC-1175 (HOM Yokotobi, carga CG000315): a Expedição grava o par Compra (8) + Saída (7). A carga
/// vincula a Saída, mas o romaneio que o contrato de compra mostra nas Entregas é a Compra — o grid da
/// carga precisa exibir o código da Compra para a conciliação visual bater.
/// </summary>
public class ShipmentLoadsPurchaseLegsServiceTests
{
    private static StorageTransaction Tx(string code, StorageTransactionType type, Guid? loadKey = null) => new()
    {
        Key = Guid.NewGuid(), Code = code, BranchCode = "01", CardCode = "F004766", ItemCode = "SOJA",
        UnitOfMeasureCode = "KG", WarehouseCode = "F003998", TruckCode = "ABC1D23",
        TransactionType = type, TransactionStatus = StorageTransactionsStatus.Confirmed,
        GrossWeight = 18_680m, NetWeight = 18_680m, ShipmentLoadKey = loadKey,
    };

    [Fact]
    public async Task Returns_the_purchase_leg_code_of_each_exit_linked_to_the_load()
    {
        var db = TestDb.CreateUnitOfWork();
        var loadKey = Guid.NewGuid();
        var purchase = Tx("00012741", StorageTransactionType.Purchase);
        var exit = Tx("00012742", StorageTransactionType.SalesShipment, loadKey);
        var otherPurchase = Tx("00099991", StorageTransactionType.Purchase);
        var otherExit = Tx("00099992", StorageTransactionType.SalesShipment, Guid.NewGuid());
        // Saída sem perna de compra (liberação de transferência de titularidade): fica de fora.
        var exitWithoutLeg = Tx("00012743", StorageTransactionType.SalesShipment, loadKey);

        db.Context.StorageTransactions.AddRange(purchase, exit, otherPurchase, otherExit, exitWithoutLeg);
        db.Context.ShippingTransactions.AddRange(
            new ShippingTransaction { Key = Guid.NewGuid(), PurchaseStorageTransactionKey = purchase.Key, SalesStorageTransactionKey = exit.Key },
            new ShippingTransaction { Key = Guid.NewGuid(), PurchaseStorageTransactionKey = otherPurchase.Key, SalesStorageTransactionKey = otherExit.Key },
            new ShippingTransaction { Key = Guid.NewGuid(), PurchaseStorageTransactionKey = null, SalesStorageTransactionKey = exitWithoutLeg.Key });
        await db.Context.SaveChangesAsync();

        var rows = await new ShipmentLoadsPurchaseLegsService(db).ExecuteAsync(loadKey);

        var row = Assert.Single(rows);
        Assert.Equal(exit.Key.ToString(), row.SalesStorageTransactionKey);
        Assert.Equal(purchase.Key.ToString(), row.PurchaseStorageTransactionKey);
        Assert.Equal("00012741", row.PurchaseCode);
    }

    [Fact]
    public void Edm_declares_the_function()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        Assert.Single(model.SchemaElements.OfType<IEdmFunction>(), f => f.Name == "ShipmentLoadsGetPurchaseLegs");
    }
}
