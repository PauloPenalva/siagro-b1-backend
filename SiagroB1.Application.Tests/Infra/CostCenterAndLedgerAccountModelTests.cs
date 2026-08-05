using System.Linq;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class CostCenterAndLedgerAccountModelTests
{
    // Usa o provider SqlServer só para materializar o modelo relacional; sem conexão.
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void CostCenter_IsMappedToExpectedTableAndColumns()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(CostCenter));
        Assert.NotNull(entityType);
        Assert.Equal("COST_CENTERS", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("Code", props);
        Assert.Contains("Name", props);
        Assert.Contains("Inactive", props);

        var key = entityType.FindPrimaryKey();
        Assert.NotNull(key);
        Assert.Equal(["Code"], key!.Properties.Select(p => p.Name));
    }

    [Fact]
    public void LedgerAccount_IsMappedToExpectedTableAndColumns()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(LedgerAccount));
        Assert.NotNull(entityType);
        Assert.Equal("LEDGER_ACCOUNTS", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains("Code", props);
        Assert.Contains("Name", props);
        Assert.Contains("Type", props);
        Assert.Contains("AllowsPosting", props);
        Assert.Contains("Inactive", props);

        var key = entityType.FindPrimaryKey();
        Assert.NotNull(key);
        Assert.Equal(["Code"], key!.Properties.Select(p => p.Name));
    }

    /// <summary>
    /// O tipo é anulável de propósito: em modo SAPB1 o SAP não fornece essa classificação.
    /// A obrigatoriedade do cadastro local é validada no LedgerAccountService.
    /// </summary>
    [Fact]
    public void LedgerAccount_Type_IsNullable()
    {
        using var context = CreateContext();

        var entityType = context.Model.FindEntityType(typeof(LedgerAccount));
        var type = entityType!.FindProperty(nameof(LedgerAccount.Type));

        Assert.NotNull(type);
        Assert.True(type!.IsNullable);
    }
}
