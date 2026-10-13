using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

/// <summary>Esquema do vínculo NF × romaneio (spec 2026-10-13 §3), lido do modelo do EF sem banco.</summary>
public class PurchaseFiscalReconciliationSchemaTests
{
    private static AppDbContext Model() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=model-inspection-only;Database=none").Options);

    [Fact]
    public void Link_table_has_unique_pair_and_restrict_fks()
    {
        using var ctx = Model();
        var entity = ctx.Model.FindEntityType(typeof(PurchaseFiscalReconciliation))!;

        Assert.Equal("PURCHASE_FISCAL_RECONCILIATIONS", entity.GetTableName());
        Assert.Contains(entity.GetIndexes(), i => i.IsUnique && i.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(PurchaseFiscalReconciliation.PurchaseInvoiceItemKey), nameof(PurchaseFiscalReconciliation.StorageTransactionKey)]));
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior));
        Assert.Equal("DECIMAL(19,6)", entity.FindProperty(nameof(PurchaseFiscalReconciliation.Quantity))!.GetColumnType());
    }

    [Fact]
    public void Storage_transaction_has_confirmation_columns()
    {
        using var ctx = Model();
        var entity = ctx.Model.FindEntityType(typeof(StorageTransaction))!;

        Assert.NotNull(entity.FindProperty(nameof(StorageTransaction.FiscalReconciledAt)));
        Assert.Equal("VARCHAR(100)", entity.FindProperty(nameof(StorageTransaction.FiscalReconciledBy))!.GetColumnType());
    }
}
