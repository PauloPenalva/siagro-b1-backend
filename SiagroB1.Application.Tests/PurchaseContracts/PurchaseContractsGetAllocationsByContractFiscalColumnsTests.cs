using SiagroB1.Application.Services.PurchaseContracts;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// GAC-1214: na aba "Entregas", devolução e reentrega não são compra do fornecedor — não podem exibir
/// fiscal 0 e "diferença" igual ao peso do romaneio, que pareceria NF do fornecedor faltando.
/// </summary>
public class PurchaseContractsGetAllocationsByContractFiscalColumnsTests
{
    [Theory]
    [InlineData("LoadRefusal")]
    [InlineData("ReturnRedelivery")]
    public void Derived_rows_have_no_fiscal_columns(string origin)
    {
        var result = PurchaseContractsGetAllocationsByContractService.BuildFiscalColumns(
            origin, physical: 30000m, fiscal: 0m, hasFiscalLink: false, fiscalReconciled: false,
            supplierInvoiceNumbers: "123", isSplitAcrossContracts: true);

        Assert.Equal(0m, result.PhysicalQuantity);
        Assert.Equal(0m, result.FiscalQuantity);
        Assert.Equal(0m, result.FiscalDifference);
        Assert.Null(result.FiscalStatus);
        Assert.Null(result.SupplierInvoiceNumbers);
        Assert.False(result.IsSplitAcrossContracts);
    }

    [Fact]
    public void Standard_purchase_row_keeps_physical_fiscal_difference_and_status()
    {
        var result = PurchaseContractsGetAllocationsByContractService.BuildFiscalColumns(
            "Standard", physical: 30000m, fiscal: 28000m, hasFiscalLink: true, fiscalReconciled: false,
            supplierInvoiceNumbers: "123", isSplitAcrossContracts: true);

        Assert.Equal(30000m, result.PhysicalQuantity);
        Assert.Equal(28000m, result.FiscalQuantity);
        Assert.Equal(-2000m, result.FiscalDifference);
        Assert.NotNull(result.FiscalStatus);
        Assert.Equal("123", result.SupplierInvoiceNumbers);
        Assert.True(result.IsSplitAcrossContracts);
    }
}
