using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Fórmula de <see cref="ShipmentLoad.TransitLossQuantity"/> (GAC-1213) — o volume que a carga
/// perdeu no trajeto (avaria, acidente, roubo) e que não chegou a ninguém.
/// </summary>
/// <remarks>
/// Só a fórmula, sem <c>SaveChanges</c> e sem estado — o molde de
/// <see cref="ShipmentLoadsRecalculateReturnedService"/>. <b>Quem GRAVA o campo é
/// <see cref="ShipmentLoadsRecalculateInvoicedService"/></b>, o escritor único do
/// <see cref="ShipmentLoad.Status"/>, que passou a depender deste termo.
/// <para>
/// <b>Por <c>GrossWeight</c></b>, como nos demais termos: o valor é subtraído de
/// <see cref="ShipmentLoad.TotalQuantity"/>, que soma o bruto dos romaneios.
/// </para>
/// </remarks>
public static class ShipmentLoadsRecalculateTransitLossService
{
    /// <summary>
    /// Σ do <c>GrossWeight</c> dos romaneios de quebra de trajeto (tipo 18) gerados pela recusa desta carga.
    /// </summary>
    /// <remarks>
    /// O vínculo é <c>RefusedFromShipmentLoadKey</c>, NUNCA <c>ShipmentLoadKey</c>. O tipo 12
    /// (devolução em armazém) não entra: é outro termo do saldo. Cancelado não conta.
    /// </remarks>
    public static async Task<decimal> CalculateTransitLossAsync(
        AppDbContext context,
        Guid shipmentLoadKey)
    {
        var loss = await context.StorageTransactions
            .Where(x => x.RefusedFromShipmentLoadKey == shipmentLoadKey &&
                        x.TransactionType == StorageTransactionType.TransitLoss &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .SumAsync(x => (decimal?)x.GrossWeight) ?? decimal.Zero;

        return decimal.Round(loss, 3, MidpointRounding.ToEven);
    }
}
