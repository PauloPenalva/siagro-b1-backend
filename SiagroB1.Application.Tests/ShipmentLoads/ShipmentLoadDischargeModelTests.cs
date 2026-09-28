using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Mapeamento relacional do registro de descarga e do anexo da carga. Usa o provider SqlServer
/// só para materializar o modelo — nenhuma conexão é aberta.
/// </summary>
public class ShipmentLoadDischargeModelTests
{
    private static AppDbContext ModelOnlyContext() => new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none")
            .Options);

    [Fact]
    public void ShipmentLoadDischarge_IsMappedToExpectedTableAndColumns()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShipmentLoadDischarge));
        Assert.NotNull(entityType);
        Assert.Equal("SHIPMENT_LOAD_DISCHARGES", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(ShipmentLoadDischarge.ShipmentLoadKey), props);
        Assert.DoesNotContain("SalesInvoiceKey", props);
        Assert.DoesNotContain("SalesInvoiceItemKey", props);
        Assert.Contains(nameof(ShipmentLoadDischarge.TicketNumber), props);
        Assert.Contains(nameof(ShipmentLoadDischarge.DischargeDate), props);
        Assert.Contains(nameof(ShipmentLoadDischarge.DischargedQuantity), props);
        Assert.Contains(nameof(ShipmentLoadDischarge.AttachmentKey), props);
    }

    [Fact]
    public void ShipmentLoadAttachment_IsMappedWithTypeColumn()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShipmentLoadAttachment));
        Assert.NotNull(entityType);
        Assert.Equal("SHIPMENT_LOAD_ATTACHMENTS", entityType!.GetTableName());

        var props = entityType.GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(ShipmentLoadAttachment.AttachmentType), props);
        Assert.Contains(nameof(ShipmentLoadAttachment.FileData), props);
    }

    [Fact]
    public void Derived_ticket_quantities_are_real_columns()
    {
        // Persistido-derivadas, não [NotMapped]: entram no EDM sem AddProperty e a tela pode
        // colocá-las no $select e no $orderby.
        using var context = ModelOnlyContext();

        var loadProps = context.Model.FindEntityType(typeof(ShipmentLoad))!
            .GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(ShipmentLoad.DischargedQuantity), loadProps);

        var itemProps = context.Model.FindEntityType(typeof(SalesInvoiceItem))!
            .GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(SalesInvoiceItem.TicketDeliveredQuantity), itemProps);
    }

    [Fact]
    public void ShipmentLoadDischargeItem_is_the_distribution_line()
    {
        using var context = ModelOnlyContext();

        var entityType = context.Model.FindEntityType(typeof(ShipmentLoadDischargeItem));
        Assert.NotNull(entityType);
        Assert.Equal("SHIPMENT_LOAD_DISCHARGE_ITEMS", entityType!.GetTableName());

        // Um ticket não rateia duas vezes a mesma linha. Sem filtro: as colunas não são anuláveis,
        // e índice filtrado exigiria QUOTED_IDENTIFIER em todo script manual.
        var unique = Assert.Single(entityType.GetIndexes(), index => index.IsUnique);
        Assert.Equal(
            new[] { nameof(ShipmentLoadDischargeItem.DischargeKey), nameof(ShipmentLoadDischargeItem.SalesInvoiceItemKey) },
            unique.Properties.Select(p => p.Name));
        Assert.Null(unique.GetFilter());
    }

    [Fact]
    public void The_distribution_goes_with_the_ticket_but_never_with_the_invoice()
    {
        using var context = ModelOnlyContext();

        var foreignKeys = context.Model.FindEntityType(typeof(ShipmentLoadDischargeItem))!
            .GetForeignKeys().ToList();

        DeleteBehavior BehaviorTo(Type principal) =>
            foreignKeys.Single(fk => fk.PrincipalEntityType.ClrType == principal).DeleteBehavior;

        Assert.Equal(DeleteBehavior.Cascade, BehaviorTo(typeof(ShipmentLoadDischarge)));
        Assert.Equal(DeleteBehavior.NoAction, BehaviorTo(typeof(SalesInvoice)));
        Assert.Equal(DeleteBehavior.NoAction, BehaviorTo(typeof(SalesInvoiceItem)));
    }

    [Fact]
    public void Discharge_foreign_keys_never_cascade()
    {
        // A exclusão da carga apaga as filhas À MÃO, em ShipmentLoadsDeleteService. Cascade aqui
        // faria a nota apagar tickets em silêncio, e o histórico do frete iria com ela.
        using var context = ModelOnlyContext();

        var foreignKeys = context.Model.FindEntityType(typeof(ShipmentLoadDischarge))!
            .GetForeignKeys().ToList();

        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.NoAction, fk.DeleteBehavior));
    }
}
