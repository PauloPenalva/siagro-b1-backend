using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Fórmula de <see cref="ShipmentLoad.ReturnedToWarehouseQuantity"/> — o volume que saiu
/// FISICAMENTE da carga de volta para um armazém, por recusa do cliente.
/// </summary>
/// <remarks>
/// Só a fórmula, sem <c>SaveChanges</c> e sem estado — o molde de
/// <see cref="ShipmentLoadsRecalculateTotalService"/>. <b>Quem GRAVA o campo é
/// <see cref="ShipmentLoadsRecalculateInvoicedService"/></b>, e não este serviço: aquele já é o
/// escritor único do <see cref="ShipmentLoad.Status"/>, e o status passou a depender deste
/// termo. Separar a gravação criaria um segundo ponto que precisa lembrar de rodar junto — que
/// é exatamente o modo de falha que o "escritor único" existe para evitar.
/// <para>
/// <b>Por <c>GrossWeight</c>, e não <c>NetWeight</c>:</b> o termo é subtraído de
/// <see cref="ShipmentLoad.TotalQuantity"/>, que soma o bruto dos romaneios. Misturar as duas
/// bases faria a subtração não significar nada. A confirmação do tipo 12 iguala os dois, mas
/// depender disso seria uma armadilha para a primeira regra de desconto que aparecesse.
/// </para>
/// </remarks>
public static class ShipmentLoadsRecalculateReturnedService
{
    /// <summary>
    /// Σ do <c>GrossWeight</c> das devoluções em armazém geradas pela recusa desta carga.
    /// </summary>
    /// <remarks>
    /// O vínculo é <c>RefusedFromShipmentLoadKey</c>, NUNCA <c>ShipmentLoadKey</c> — ver o
    /// XML-doc daquela propriedade. O filtro de tipo é redundante hoje (só a recusa preenche a
    /// coluna) e é a garantia de que continue significando "devolução" se alguém a reutilizar.
    /// Cancelado não conta, como em todo somatório do projeto.
    /// </remarks>
    public static async Task<decimal> CalculateReturnedToWarehouseAsync(
        AppDbContext context,
        Guid shipmentLoadKey)
    {
        var returned = await context.StorageTransactions
            .Where(x => x.RefusedFromShipmentLoadKey == shipmentLoadKey &&
                        x.TransactionType == StorageTransactionType.SalesShipmentReturn &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .SumAsync(x => (decimal?)x.GrossWeight) ?? decimal.Zero;

        return decimal.Round(returned, 3, MidpointRounding.ToEven);
    }
}
