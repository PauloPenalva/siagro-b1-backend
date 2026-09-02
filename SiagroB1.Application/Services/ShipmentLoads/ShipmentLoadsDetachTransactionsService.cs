using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Desvincula romaneios de embarque de uma carga, devolvendo-os à lista de disponíveis.
/// </summary>
/// <remarks>
/// <b>O guard daqui é o que protege a invariante I2 pelo lado que o guard de faturamento não
/// vigia.</b> O <c>ShipmentLoadsBillingGuardService</c> valida o que ENTRA — que a soma das
/// notas não ultrapasse o volume da carga. Ele nada pode contra o volume ENCOLHER por baixo de
/// notas já emitidas, que é exatamente o que tirar romaneio de uma carga faturada faria. Por
/// isso a desvinculação exige carga sem nenhum documento de saída vivo.
/// <para>
/// A checagem é pelas NOTAS, não pelo status, pelo mesmo motivo documentado em
/// <see cref="ShipmentLoadsCancelService"/>: durante o faturamento parcial a carga segue em
/// <c>PartiallyInvoiced</c>, mas é a existência da nota que importa — status é derivado e
/// oscila.
/// </para>
/// <para>
/// Devolver o romaneio é zerar <c>ShipmentLoadKey</c> e voltar o <c>TransactionStatus</c> para
/// <c>Confirmed</c>, que é o filtro da tela de vinculação. Romaneio <c>Cancelled</c> ou
/// <c>Returned</c> nunca é reescrito: esses estados são dele, não projeção da carga.
/// </para>
/// </remarks>
public class ShipmentLoadsDetachTransactionsService(
    IUnitOfWork db,
    ShipmentLoadsCompositionGuardService compositionGuard,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task<ShipmentLoad> ExecuteAsync(
        Guid shipmentLoadKey,
        ICollection<Guid> storageTransactionKeys,
        string userName)
    {
        if (storageTransactionKeys.Count == 0)
            throw new ApplicationException("Selecione ao menos um romaneio para desvincular.");

        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == shipmentLoadKey) ??
                   throw new NotFoundException($"Shipment load not found key {shipmentLoadKey}");

        if (load.Status == ShipmentLoadStatus.Cancelled)
            throw new ApplicationException(
                $"A carga {load.Code} está cancelada — seus romaneios já foram devolvidos.");

        await compositionGuard.EnsureCanChangeCompositionAsync(load);

        var distinctKeys = storageTransactionKeys.Distinct().ToList();

        var shipments = await db.Context.StorageTransactions
            .Where(x => distinctKeys.Contains(x.Key))
            .ToListAsync();

        if (shipments.Count != distinctKeys.Count)
            throw new ApplicationException("Romaneio de embarque não encontrado.");

        var foreign = shipments.FirstOrDefault(x => x.ShipmentLoadKey != load.Key);
        if (foreign != null)
            throw new ApplicationException(
                $"O romaneio {foreign.Code} não pertence à carga {load.Code}.");

        var detachedQuantity = decimal.Round(
            shipments.Sum(x => x.GrossWeight), 3, MidpointRounding.ToEven);

        try
        {
            await db.BeginTransactionAsync();

            foreach (var shipment in shipments)
            {
                shipment.ShipmentLoadKey = null;

                if (shipment.TransactionStatus is not (StorageTransactionsStatus.Cancelled
                    or StorageTransactionsStatus.Returned))
                {
                    shipment.TransactionStatus = StorageTransactionsStatus.Confirmed;
                }

                shipment.UpdatedAt = DateTime.Now;
                shipment.UpdatedBy = userName;
            }

            // Igual à vinculação: o recálculo do total consulta o banco, então as FKs zeradas
            // precisam estar gravadas antes.
            await db.SaveChangesAsync();

            await ShipmentLoadsRecalculateTotalService.RecalculateAsync(db.Context, load.Key);
            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            load.UpdatedBy = userName;

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.TransactionsDetached,
                -detachedQuantity,
                load.AvailableQuantity,
                $"{shipments.Count} romaneio(s) desvinculado(s) da carga: " +
                string.Join(", ", shipments.Select(x => x.Code)),
                userName);

            await db.SaveChangesAsync();

            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        return load;
    }

}
