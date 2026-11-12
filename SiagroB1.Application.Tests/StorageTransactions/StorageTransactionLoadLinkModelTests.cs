using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.StorageTransactions;

/// <summary>
/// GAC-1216: a carga do romaneio vem da view <c>STORAGE_TRANSACTIONS_LOAD_LINKS</c>, exposta como a
/// navegação <c>LoadLink</c> — é o que deixa as listas filtrarem e ordenarem pela carga no SQL.
/// </summary>
public class StorageTransactionLoadLinkModelTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void Load_link_is_mapped_to_the_view_and_owns_no_table()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(StorageTransactionLoadLink))!;

        Assert.Equal("STORAGE_TRANSACTIONS_LOAD_LINKS", entity.GetViewName());
        Assert.Null(entity.GetTableName());
    }

    [Fact]
    public void Sorting_transactions_by_load_code_joins_the_view()
    {
        using var context = CreateContext();

        var sql = context.StorageTransactions.OrderBy(x => x.LoadLink!.LoadCode).ToQueryString();

        Assert.Contains("LEFT JOIN [STORAGE_TRANSACTIONS_LOAD_LINKS]", sql);
    }

    [Fact]
    public void Filtering_allocations_by_load_code_joins_the_view()
    {
        using var context = CreateContext();

        var sql = context.PurchaseContractsAllocations
            .Where(x => x.StorageTransaction!.LoadLink!.LoadCode!.Contains("CG"))
            .ToQueryString();

        Assert.Contains("[STORAGE_TRANSACTIONS_LOAD_LINKS]", sql);
    }

    /// <summary>
    /// A view é testada num banco mínimo (<see cref="StorageTransactionLoadLinkViewTests"/>): se uma
    /// destas colunas for renomeada no modelo, a view quebra em produção e o teste dela não vê.
    /// </summary>
    [Theory]
    [InlineData(typeof(ShipmentLoad), "SHIPMENT_LOADS", "Code")]
    [InlineData(typeof(StorageTransaction), "STORAGE_TRANSACTIONS", "ShipmentLoadKey")]
    [InlineData(typeof(StorageTransaction), "STORAGE_TRANSACTIONS", "RefusedFromShipmentLoadKey")]
    [InlineData(typeof(StorageTransaction), "STORAGE_TRANSACTIONS", "ShipmentLoadTransshipmentKey")]
    [InlineData(typeof(StorageTransaction), "STORAGE_TRANSACTIONS", "ReplacedByShippingReleaseChangeKey")]
    [InlineData(typeof(StorageTransaction), "STORAGE_TRANSACTIONS", "ShippingReleaseChangeKey")]
    [InlineData(typeof(StorageTransaction), "STORAGE_TRANSACTIONS", "DischargeSalesInvoiceItemKey")]
    [InlineData(typeof(ShippingTransaction), "SHIPPING_TRANSACTIONS", "RowId")]
    [InlineData(typeof(ShippingTransaction), "SHIPPING_TRANSACTIONS", "PurchaseStorageTransactionKey")]
    [InlineData(typeof(ShippingTransaction), "SHIPPING_TRANSACTIONS", "SalesStorageTransactionKey")]
    [InlineData(typeof(ShipmentLoadTransshipment), "SHIPMENT_LOAD_TRANSSHIPMENTS", "ShipmentLoadKey")]
    [InlineData(typeof(ShippingReleaseChange), "SHIPPING_RELEASE_CHANGES", "ShipmentLoadKey")]
    [InlineData(typeof(SalesInvoice), "SALES_INVOICES", "ShipmentLoadKey")]
    [InlineData(typeof(SalesInvoiceItem), "SALES_INVOICES_ITEMS", "SalesInvoiceKey")]
    public void Columns_read_by_the_view_exist_in_the_model(Type clrType, string table, string column)
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(clrType)!;

        Assert.Equal(table, entity.GetTableName());
        Assert.Equal(column, entity.FindProperty(column)?.GetColumnName());
    }

    [Fact]
    public void Edm_exposes_the_load_link_navigation_with_the_load_code()
    {
        var builder = new ODataConventionModelBuilder { Namespace = "SIAGROB1" };
        builder.ConfigureODataEntities();
        var transaction = builder.GetEdmModel().EntityContainer.FindEntitySet("StorageTransactions")!.EntityType;

        var navigation = Assert.IsAssignableFrom<IEdmNavigationProperty>(transaction.FindProperty("LoadLink"));
        Assert.NotNull(navigation.ToEntityType().FindProperty("LoadCode"));
    }
}
