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

        // GAC-1175: a conclusão da carga de remoção é a afirmação de que a remoção terminou.
        if (load.Status == ShipmentLoadStatus.Completed)
            throw new ApplicationException(
                $"A carga {load.Code} já foi concluída. " +
                $"{ShipmentLoadCompletionRules.UndoHint(load)} antes de alterar a composição.");

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

        await EnsureNoOriginExitWhileThereIsATransshipmentAsync(load, shipments);

        var detachedQuantity = decimal.Round(
            shipments.Sum(x => x.GrossWeight), 3, MidpointRounding.ToEven);

        try
        {
            await db.BeginTransactionAsync();

            foreach (var shipment in shipments)
            {
                shipment.ShipmentLoadKey = null;

                // GAC-1181: zera também o papel do transbordo, se houver — é o que permite
                // corrigir uma saída vinculada por engano (ver EnsureNoOriginExitWhileThere...).
                shipment.ShipmentLoadTransshipmentKey = null;

                // A carga de REMOÇÃO nunca projetou status no romaneio (ver
                // ShipmentLoadsRecalculateInvoicedService), então não há o que desfazer aqui — e
                // reescrever o Recebimento para Confirmed apagaria um Invoiced vindo de outro
                // fluxo, alheio à carga.
                if (load.LoadType != ShipmentLoadType.Removal &&
                    shipment.TransactionStatus is not (StorageTransactionsStatus.Cancelled
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

    /// <summary>
    /// GAC-1181: enquanto a carga tiver transbordo (qualquer um, aberto ou não — o estorno é que
    /// apaga a linha), a saída da ORIGEM (<c>ShipmentLoadTransshipmentKey == null</c>) não pode
    /// ser desvinculada: é ela que <c>ShipmentLoadsTransshipmentRegisterEntryService</c> usa para
    /// ratear as liberações emitidas na entrada. A saída do PRÓPRIO transbordo não entra nesta
    /// trava — desvinculá-la é o único jeito de corrigir um vínculo feito por engano (ver
    /// <c>ShipmentLoadsCompositionGuardService</c>, que por isso não barra o Detach inteiro).
    /// </summary>
    private async Task EnsureNoOriginExitWhileThereIsATransshipmentAsync(
        ShipmentLoad load, List<StorageTransaction> shipments)
    {
        var originShipment = shipments.FirstOrDefault(x => x.ShipmentLoadTransshipmentKey == null);
        if (originShipment == null)
            return;

        var hasTransshipment = await db.Context.ShipmentLoadsTransshipments
            .AnyAsync(x => x.ShipmentLoadKey == load.Key);

        if (hasTransshipment)
            throw new ApplicationException(
                $"O romaneio {originShipment.Code} é a saída de origem da carga {load.Code}, que " +
                "tem transbordo. As liberações do transbordo dependem dele — estorne o transbordo " +
                "antes de desvinculá-lo.");
    }
}
