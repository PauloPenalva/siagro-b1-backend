using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

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
        string? invoiceNumber = null)
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
