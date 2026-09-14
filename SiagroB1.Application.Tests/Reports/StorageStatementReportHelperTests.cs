using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Helpers;

namespace SiagroB1.Application.Tests.Reports;

public class StorageStatementReportHelperTests
{
    [Fact]
    public void Warehouse_gain_is_an_entry_labelled_sobra()
    {
        Assert.Equal("Sobra Armazém",
            StorageStatementReportHelper.GetTransactionTypeName(StorageTransactionType.WarehouseGain));
        Assert.True(StorageStatementReportHelper.IsEntry(StorageTransactionType.WarehouseGain));
        Assert.Equal(10m,
            StorageStatementReportHelper.GetSignedQuantity(StorageTransactionType.WarehouseGain, 10m));
    }

    [Fact]
    public void Warehouse_loss_is_an_exit_labelled_perda()
    {
        Assert.Equal("Perda Armazém",
            StorageStatementReportHelper.GetTransactionTypeName(StorageTransactionType.WarehouseLoss));
        Assert.True(StorageStatementReportHelper.IsExit(StorageTransactionType.WarehouseLoss));
        Assert.Equal(-10m,
            StorageStatementReportHelper.GetSignedQuantity(StorageTransactionType.WarehouseLoss, 10m));
    }
}
