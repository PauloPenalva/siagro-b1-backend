using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Mapeamento relacional da Carga. Usa o provider SqlServer só para materializar o modelo —
/// nenhuma conexão é aberta.
/// </summary>
public class ShipmentLoadModelTests
{
    private static ShipmentLoad NewLoad(decimal total, decimal invoiced) => new()
    {
        ItemCode = "SOJA",
        UnitOfMeasureCode = "KG",
        TotalQuantity = total,
        InvoicedQuantity = invoiced,
    };

    private static AppDbContext ModelOnlyContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void ShipmentLoad_IsMappedToExpectedTableAndColumns()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShipmentLoad));
        Assert.NotNull(entityType);
        Assert.Equal("SHIPMENT_LOADS", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(ShipmentLoad.Code), props);
        Assert.Contains(nameof(ShipmentLoad.LoadDate), props);
        Assert.Contains(nameof(ShipmentLoad.Status), props);
        Assert.Contains(nameof(ShipmentLoad.ItemCode), props);
        Assert.Contains(nameof(ShipmentLoad.TruckCode), props);
        Assert.Contains(nameof(ShipmentLoad.TotalQuantity), props);
        Assert.Contains(nameof(ShipmentLoad.InvoicedQuantity), props);
        Assert.Contains(nameof(ShipmentLoad.BranchCode), props);
        Assert.Contains(nameof(ShipmentLoad.DocNumberKey), props);
    }

    [Fact]
    public void ShipmentLoad_derived_quantities_stay_out_of_the_table()
    {
        // AvailableQuantity e IsFullyInvoiced são [NotMapped]: derivam de TotalQuantity e
        // InvoicedQuantity e não podem virar coluna, senão passam a ter dono para atualizar.
        using var context = ModelOnlyContext();

        var props = context.Model.FindEntityType(typeof(ShipmentLoad))!
            .GetProperties().Select(p => p.Name).ToArray();

        Assert.DoesNotContain(nameof(ShipmentLoad.AvailableQuantity), props);
        Assert.DoesNotContain(nameof(ShipmentLoad.IsFullyInvoiced), props);
    }

    [Fact]
    public void ShipmentLoad_HasRowVersionConcurrencyToken()
    {
        // É a ÚNICA proteção real contra duas notas parciais simultâneas: os dois guards
        // passam em paralelo e só o rowversion derruba a segunda no SaveChanges.
        using var context = ModelOnlyContext();

        var rowVersion = context.Model.FindEntityType(typeof(ShipmentLoad))!
            .FindProperty(nameof(ShipmentLoad.RowVersion));

        Assert.NotNull(rowVersion);
        Assert.True(rowVersion!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, rowVersion.ValueGenerated);
    }

    [Fact]
    public void ShipmentLoad_Code_IsUnique()
    {
        using var context = ModelOnlyContext();

        var index = context.Model.FindEntityType(typeof(ShipmentLoad))!
            .GetIndexes()
            .SingleOrDefault(i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(ShipmentLoad.Code)]));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }

    [Fact]
    public void ShipmentLoadMovement_IsMappedToExpectedTable()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShipmentLoadMovement));
        Assert.NotNull(entityType);
        Assert.Equal("SHIPMENT_LOAD_MOVEMENTS", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(ShipmentLoadMovement.ShipmentLoadKey), props);
        Assert.Contains(nameof(ShipmentLoadMovement.MovementType), props);
        Assert.Contains(nameof(ShipmentLoadMovement.Quantity), props);
        Assert.Contains(nameof(ShipmentLoadMovement.BalanceAfter), props);
        Assert.Contains(nameof(ShipmentLoadMovement.SalesInvoiceKey), props);
        Assert.Contains(nameof(ShipmentLoadMovement.InvoiceNumber), props);
        Assert.Contains(nameof(ShipmentLoadMovement.Description), props);
    }

    [Fact]
    public void ShipmentLoadMovement_SalesInvoiceKey_HasNoForeignKey()
    {
        // Sem FK de propósito: SalesInvoicesDeleteService apaga a nota pendente e todas as FKs
        // do projeto são NoAction — com FK real o delete quebraria. O histórico sobrevive
        // pelo InvoiceNumber desnormalizado, que é o que o usuário lê.
        using var context = ModelOnlyContext();

        var foreignKeys = context.Model.FindEntityType(typeof(ShipmentLoadMovement))!
            .GetForeignKeys()
            .SelectMany(fk => fk.Properties)
            .Select(p => p.Name)
            .ToArray();

        Assert.DoesNotContain(nameof(ShipmentLoadMovement.SalesInvoiceKey), foreignKeys);
        Assert.Contains(nameof(ShipmentLoadMovement.ShipmentLoadKey), foreignKeys);
    }

    [Fact]
    public void StorageTransaction_and_SalesInvoice_carry_the_load_key()
    {
        // FK escalar nos dois lados: é o único desenho em que o BANCO proíbe
        // "romaneio em duas cargas" e "invoice consumindo duas cargas".
        using var context = ModelOnlyContext();

        var transaction = context.Model.FindEntityType(typeof(StorageTransaction))!;
        Assert.NotNull(transaction.FindProperty(nameof(StorageTransaction.ShipmentLoadKey)));
        Assert.Contains(
            transaction.GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(StorageTransaction.ShipmentLoadKey)]));

        var invoice = context.Model.FindEntityType(typeof(SalesInvoice))!;
        Assert.NotNull(invoice.FindProperty(nameof(SalesInvoice.ShipmentLoadKey)));
        Assert.Contains(
            invoice.GetIndexes(),
            i => i.Properties.Select(p => p.Name).SequenceEqual([nameof(SalesInvoice.ShipmentLoadKey)]));
    }

    [Fact]
    public void TransactionCode_ShipmentLoad_is_eleven()
    {
        // O seed de DOC_NUMBERS grava TransactionCode = 11 em SQL bruto; mudar o enum sem
        // mudar a migration deixaria a numeração órfã e a primeira carga falharia.
        Assert.Equal(11, (int)TransactionCode.ShipmentLoad);
    }

    [Theory]
    [InlineData(90, 0, 90)]
    [InlineData(90, 40, 50)]
    [InlineData(90, 90, 0)]
    public void AvailableQuantity_is_total_minus_invoiced(decimal total, decimal invoiced, decimal expected)
    {
        var load = NewLoad(total, invoiced);

        Assert.Equal(expected, load.AvailableQuantity);
    }

    [Fact]
    public void AvailableQuantity_is_zero_when_the_load_is_cancelled()
    {
        var load = NewLoad(90, 0);
        load.Status = ShipmentLoadStatus.Cancelled;

        Assert.Equal(decimal.Zero, load.AvailableQuantity);
        Assert.True(load.IsFullyInvoiced);
    }

    [Fact]
    public void AvailableQuantity_rounds_to_three_decimals()
    {
        var load = NewLoad(10.0005m, 0);

        Assert.Equal(10.000m, load.AvailableQuantity);
    }
}
