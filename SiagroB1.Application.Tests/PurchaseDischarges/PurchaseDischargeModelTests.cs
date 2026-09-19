using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseDischarges;

/// <summary>
/// Troca nota: os romaneios de quebra/sobra de descarga (16/17) acertam o contrato de compra CIF e
/// NADA mais — nem o saldo do armazém do par 8/7 (onde o grão nunca esteve), nem o embarcado da
/// liberação (ela registra o que foi embarcado). Os vínculos são colunas próprias, sem navegação.
/// </summary>
public class PurchaseDischargeModelTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    [Fact]
    public void Enum_values_are_stable()
    {
        Assert.Equal(16, (int)StorageTransactionType.PurchaseDischargeLoss);
        Assert.Equal(17, (int)StorageTransactionType.PurchaseDischargeGain);
        Assert.Equal(14, (int)TransactionCode.PurchaseDischarge);
    }

    [Theory]
    [InlineData(nameof(StorageTransaction.DischargeSalesInvoiceItemKey), typeof(SalesInvoiceItem))]
    [InlineData(nameof(StorageTransaction.DischargeShippingTransactionKey), typeof(ShippingTransaction))]
    public void Link_columns_are_NoAction_foreign_keys(string property, Type principal)
    {
        var entity = _db.Context.Model.FindEntityType(typeof(StorageTransaction))!;
        var fk = entity.GetForeignKeys().Single(f => f.Properties.Single().Name == property);

        Assert.Equal(principal, fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior);
        Assert.Null(fk.DependentToPrincipal);
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss)]
    [InlineData(StorageTransactionType.PurchaseDischargeGain)]
    public async Task Discharge_transactions_do_not_move_the_warehouse_balance(StorageTransactionType type)
    {
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "D1", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", WarehouseCode = "PORTO", TransactionType = type,
            TransactionStatus = StorageTransactionsStatus.Confirmed, GrossWeight = 500m, NetWeight = 500m,
        });
        await _db.Context.SaveChangesAsync();

        Assert.Equal(0m, await StorageTransactionsWarehouseBalanceService.CalculateAsync(_db.Context, "PORTO", "SOJA"));
    }

    [Theory]
    [InlineData(StorageTransactionType.PurchaseDischargeLoss)]
    [InlineData(StorageTransactionType.PurchaseDischargeGain)]
    public void Discharge_transactions_do_not_consume_releases(StorageTransactionType type)
    {
        Assert.False(ShipmentReleasesRecalculateShippedService.AffectsShippedQuantity(type));
        Assert.Equal(0, ShipmentReleasesRecalculateShippedService.ShippedSign(ReleaseOrigin.Standard, type));
    }
}
