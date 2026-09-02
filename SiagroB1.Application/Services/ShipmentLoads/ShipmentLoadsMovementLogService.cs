using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Contexto do movimento: para ONDE a carga foi e POR QUE voltou.
/// </summary>
/// <remarks>
/// É o que transforma a movimentação na narrativa que o financeiro lê para pagar o frete:
/// <c>Faturamento → cliente A, local A · Recusa → motivo · Devolução Confirmada ·
/// Devolvida ao Armazém → armazém X · Faturamento → cliente B, local B</c>.
/// <para>
/// Record com todos os membros opcionais, e parâmetro opcional no <c>Register</c>, para que os
/// pontos de chamada que não têm contexto a acrescentar sigam como estão — nenhum deles fica
/// pior por causa dos que ficam melhores.
/// </para>
/// </remarks>
public sealed record ShipmentLoadMovementContext(
    string? CardCode = null,
    string? CardName = null,
    string? DeliveryCardCode = null,
    string? DeliveryCardName = null,
    string? WarehouseCode = null,
    string? WarehouseName = null,
    string? Reason = null,
    Guid? StorageTransactionKey = null)
{
    /// <summary>Contexto comercial lido do documento de saída que o movimento narra.</summary>
    public static ShipmentLoadMovementContext FromInvoice(SalesInvoice invoice, string? reason = null) =>
        new(invoice.CardCode,
            invoice.CardName,
            invoice.DeliveryCardCode,
            invoice.DeliveryCardName,
            Reason: reason);
}

/// <summary>
/// Porta única de escrita do histórico de movimentação da carga.
///
/// Apenas enfileira a linha no contexto — quem chama decide quando salvar, para que o movimento
/// e a alteração de saldo que ele descreve entrem no mesmo <c>SaveChanges</c> (nunca sobra
/// movimento de um efeito que falhou, nem efeito sem movimento). Espelho de
/// <c>SalesInvoicesChangeLogService</c>.
/// </summary>
/// <remarks>
/// O histórico é NARRATIVA, não autoridade — ver <see cref="ShipmentLoadMovement"/>. Nada aqui
/// é lido de volta para compor saldo.
/// </remarks>
public class ShipmentLoadsMovementLogService(AppDbContext context)
{
    public void Register(
        Guid shipmentLoadKey,
        ShipmentLoadMovementType movementType,
        decimal quantity,
        decimal balanceAfter,
        string description,
        string userName,
        Guid? salesInvoiceKey = null,
        string? invoiceNumber = null,
        ShipmentLoadMovementContext? movementContext = null)
    {
        context.ShipmentLoadMovements.Add(new ShipmentLoadMovement
        {
            ShipmentLoadKey = shipmentLoadKey,
            MovementType = movementType,
            Quantity = quantity,
            BalanceAfter = balanceAfter,
            SalesInvoiceKey = salesInvoiceKey,
            InvoiceNumber = invoiceNumber,
            Description = Truncate(description),
            CardCode = movementContext?.CardCode,
            CardName = movementContext?.CardName,
            DeliveryCardCode = movementContext?.DeliveryCardCode,
            DeliveryCardName = movementContext?.DeliveryCardName,
            WarehouseCode = movementContext?.WarehouseCode,
            WarehouseName = movementContext?.WarehouseName,
            Reason = Truncate(movementContext?.Reason),
            StorageTransactionKey = movementContext?.StorageTransactionKey,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName,
        });
    }

    /// <summary>
    /// A coluna é VARCHAR(500): um texto longo não pode derrubar a gravação do efeito que o
    /// movimento só acompanha.
    /// </summary>
    private static string? Truncate(string? value) =>
        value is { Length: > 500 } ? value[..500] : value;
}
