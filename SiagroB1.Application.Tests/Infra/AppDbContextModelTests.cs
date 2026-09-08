using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Infra;

public class AppDbContextModelTests
{
    /// <summary>
    /// Materializa o modelo relacional com o provider SqlServer. Nenhuma conexão é aberta —
    /// é só para inspecionar o mapeamento.
    /// </summary>
    private static AppDbContext ModelOnlyContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void DeliveryDifference_IsStoredComputedColumn()
    {
        // A Diferença é derivada e mantida pelo SQL Server. Se alguém trocar isso por coluna
        // comum sem atualizar os serviços que escrevem Quantity/DeliveredQuantity, a tela de
        // conferência passa a mostrar número defasado — este teste é a trava.
        // Sob EF InMemory a computed column não é avaliada (o valor fica 0), então o
        // comportamento não dá para exercitar em teste; só o mapeamento.
        using var context = ModelOnlyContext();

        var property = context.Model
            .FindEntityType(typeof(SalesInvoiceItem))!
            .FindProperty(nameof(SalesInvoiceItem.DeliveryDifference))!;

        Assert.Equal(
            "CASE WHEN [DeliveredQuantity] = 0 AND [DeliveryStatus] = 0 THEN 0 " +
            "ELSE [DeliveredQuantity] - [Quantity] END",
            property.GetComputedColumnSql());
        Assert.True(property.GetIsStored());
    }

    [Fact]
    public void AllColumnTypes_HaveBalancedParentheses()
    {
        // Guarda contra TypeName malformado como "DECIMAL(18,3) DEFAULT 0)" —
        // parêntese sobrando gera DDL inválido e obriga edição manual de migrations.
        // Usa o provider SqlServer apenas para materializar o modelo relacional;
        // nenhuma conexão é aberta.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options;

        using var context = new AppDbContext(options);

        // Inspeciona a annotation crua (o que o snapshot/migrations serializam),
        // não GetColumnType() — o type mapper normaliza e esconderia o defeito
        var malformed = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties(), (e, p) => new
            {
                Entity = e.ClrType.Name,
                Property = p.Name,
                ColumnType = (string?)p.FindAnnotation("Relational:ColumnType")?.Value,
            })
            .Where(x => x.ColumnType != null
                        && x.ColumnType.Count(c => c == '(') != x.ColumnType.Count(c => c == ')'))
            .Select(x => $"{x.Entity}.{x.Property}: \"{x.ColumnType}\"")
            .ToList();

        Assert.True(malformed.Count == 0,
            "Colunas com tipo malformado (parênteses desbalanceados):\n" + string.Join("\n", malformed));
    }

    [Fact]
    public void ShipmentReleaseGeneratedByStorageTransaction_IsNotPairedWithTransactions()
    {
        // As duas relações entre SHIPMENT_RELEASES e STORAGE_TRANSACTIONS têm significados
        // OPOSTOS: Transactions são os romaneios que CONSOMEM a liberação;
        // GeneratedByStorageTransaction é o romaneio de devolução que a CRIOU.
        // Se a convenção do EF parear a segunda com a coleção Transactions, o romaneio tipo 12
        // passa a contar como romaneio da liberação — e como ele entra SUBTRAINDO no eixo de
        // venda de CalculateShippedAsync, o saldo da liberação nasce NEGATIVO e a Expedição de
        // Grãos oferece o dobro do grão que voltou. Falha silenciosa; este teste é a trava.
        using var context = ModelOnlyContext();

        var releaseType = context.Model.FindEntityType(typeof(ShipmentRelease))!;

        var generatedBy = releaseType
            .GetForeignKeys()
            .Single(fk => fk.Properties.Any(p =>
                p.Name == nameof(ShipmentRelease.GeneratedByStorageTransactionKey)));

        Assert.Equal(typeof(StorageTransaction), generatedBy.PrincipalEntityType.ClrType);

        // Sem coleção inversa: o romaneio não navega de volta para a liberação que gerou.
        Assert.Null(generatedBy.PrincipalToDependent);
        Assert.Equal(
            nameof(ShipmentRelease.GeneratedByStorageTransaction),
            generatedBy.DependentToPrincipal!.Name);

        // E a coleção Transactions continua sendo alimentada pela OUTRA ponta.
        var consuming = context.Model
            .FindEntityType(typeof(StorageTransaction))!
            .GetForeignKeys()
            .Single(fk => fk.Properties.Any(p =>
                p.Name == nameof(StorageTransaction.ShipmentReleaseKey)));

        Assert.Equal(
            nameof(ShipmentRelease.Transactions),
            consuming.PrincipalToDependent!.Name);
    }
}
