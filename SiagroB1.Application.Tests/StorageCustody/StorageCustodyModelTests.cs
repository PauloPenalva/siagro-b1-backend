using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.StorageCustody;

/// <summary>Notas de custódia (spec 2026-10-12 §3): FKs Restrict para o romaneio e campos expostos no EDM.</summary>
public class StorageCustodyModelTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Theory]
    [InlineData(typeof(PurchaseInvoice))]
    [InlineData(typeof(SalesInvoice))]
    public void Custody_transaction_key_is_a_restrict_fk(Type invoice)
    {
        using var context = CreateContext();

        var fk = context.Model.FindEntityType(invoice)!
            .GetForeignKeys()
            .Single(x => x.Properties.Single().Name == "CustodyStorageTransactionKey");

        Assert.Equal(typeof(StorageTransaction), fk.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void Role_and_processing_flag_are_not_nullable()
    {
        using var context = CreateContext();

        Assert.False(context.Model.FindEntityType(typeof(UsageEffect))!.FindProperty("StorageCustodyRole")!.IsNullable);
        Assert.False(context.Model.FindEntityType(typeof(StorageAddress))!.FindProperty("RequiresProcessing")!.IsNullable);
        Assert.False(context.Model.FindEntityType(typeof(PurchaseInvoice))!.FindProperty("CustodyRole")!.IsNullable);
        Assert.False(context.Model.FindEntityType(typeof(SalesInvoice))!.FindProperty("CustodyRole")!.IsNullable);
    }

    [Fact]
    public void Edm_exposes_the_new_fields()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();
        IEdmEntityType Type(string name) => model.SchemaElements.OfType<IEdmEntityType>().Single(t => t.Name == name);

        Assert.NotNull(Type(nameof(PurchaseInvoice)).FindProperty("CustodyStorageTransactionKey"));
        Assert.NotNull(Type(nameof(PurchaseInvoice)).FindProperty("CustodyRole"));
        Assert.NotNull(Type(nameof(SalesInvoice)).FindProperty("CustodyStorageTransactionKey"));
        Assert.NotNull(Type(nameof(StorageAddress)).FindProperty("RequiresProcessing"));
        Assert.NotNull(Type(nameof(UsageModel)).FindProperty("StorageCustodyRole"));
    }
}
