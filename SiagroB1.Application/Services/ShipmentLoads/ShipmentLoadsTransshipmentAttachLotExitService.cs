using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Vincula à carga o romaneio <see cref="StorageTransactionType.Shipment"/> (1) que o armazém
/// pesou ao recarregar o grão de um transbordo em armazém PRÓPRIO — a saída do LOTE (GAC-1181
/// fase 2, Task 4). É este vínculo que EMITE a liberação, pela quantidade REAL carregada.
/// </summary>
/// <remarks>
/// Par de <see cref="ShipmentLoadsTransshipmentRegisterEntryService"/> (Task 3 desta fase): aquele
/// registra a entrada, este vincula a saída.
/// <para>
/// <b>Por que a liberação nasce AQUI e não na entrada.</b> A entrada (Task 3) credita só o
/// ARMAZÉM (o <c>TransshipmentReceipt</c>, o 15) e não emite liberação nenhuma para armazém
/// próprio — o peso que sai do lote só é conhecido no carregamento. Emitir a liberação na entrada
/// pela quantidade pesada ali faria a sobra existir DUAS vezes: como saldo de liberação (nunca
/// carregado) e como saldo de lote (o que sobrou depois do carregamento real). A sobra fica só no
/// lote, e só nele.
/// </para>
/// <para>
/// <b>A liberação nasce SEM <see cref="ShipmentRelease.StorageAddressCode"/>.</b> O LOTE já foi
/// debitado por este mesmo romaneio (o <c>Shipment</c> tipo 1) — se a liberação carregasse o
/// lote, a Expedição de Grãos do passo seguinte (<see cref="StorageTransactionType.SalesShipment"/>,
/// o 7) o drenaria uma SEGUNDA vez. Quem já garante isso é
/// <see cref="ShipmentReleasesFromReturnService"/>: toda liberação que ele monta nasce sem lote —
/// não é uma decisão tomada aqui, é herdada.
/// </para>
/// <para>
/// <b>Vincular esta saída NÃO conclui o transbordo.</b> A carga continua em
/// <see cref="ShipmentLoadStatus.InTransshipment"/> depois deste serviço:
/// <c>ShipmentLoadsRecalculateTransshippedService.HasOpenTransshipmentAsync</c> só enxerga o tipo
/// <see cref="StorageTransactionType.SalesShipment"/>, não o <c>Shipment</c> vinculado aqui. Quem
/// fecha de verdade — e libera o faturamento — é a Expedição de venda vinculada depois pelo
/// caminho de sempre (<c>ShipmentLoadsAttachTransactionsService</c>), numa task posterior.
/// </para>
/// <para>
/// <b>Tudo numa transação só</b>, mesmo precedente de
/// <see cref="ShipmentLoadsTransshipmentRegisterEntryService"/>: recalcular e registrar o
/// movimento só DEPOIS do <c>SaveChangesAsync</c> que gravou as FKs — o recálculo consulta o
/// banco e não enxerga o change tracker.
/// </para>
/// </remarks>
public class ShipmentLoadsTransshipmentAttachLotExitService(
    IUnitOfWork db,
    IWarehouseService warehouseService,
    ShipmentReleasesFromReturnService returnReleases,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task<ShipmentLoadTransshipment> ExecuteAsync(
        Guid transshipmentKey, Guid lotExitStorageTransactionKey, string userName)
    {
        var transshipment = await db.Context.ShipmentLoadsTransshipments
                                 .FirstOrDefaultAsync(x => x.Key == transshipmentKey) ??
                             throw new NotFoundException(
                                 $"Shipment load transshipment not found key {transshipmentKey}");

        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == transshipment.ShipmentLoadKey) ??
                   throw new NotFoundException(
                       $"Shipment load not found key {transshipment.ShipmentLoadKey}");

        // TODA a validação antes de qualquer escrita: um vínculo recusado não pode deixar efeito
        // no banco.
        ShipmentLoadTransshipmentRules.EnsureLoadAcceptsTransshipment(load);
        ShipmentLoadTransshipmentRules.EnsureEntryIsRegistered(transshipment);
        ShipmentLoadTransshipmentRules.EnsureLotExitNotAlreadyAttached(transshipment);

        var entryReceipt = await db.Context.StorageTransactions
                                .FirstOrDefaultAsync(x => x.Key == transshipment.EntryStorageTransactionKey!.Value) ??
                            throw new NotFoundException(
                                $"Storage transaction not found key {transshipment.EntryStorageTransactionKey}");

        var lotExit = await db.Context.StorageTransactions
                          .FirstOrDefaultAsync(x => x.Key == lotExitStorageTransactionKey) ??
                      throw new NotFoundException(
                          $"Storage transaction not found key {lotExitStorageTransactionKey}");

        await ShipmentLoadTransshipmentRules.EnsureLotExitIsUsableAsync(
            db.Context, lotExit, load, transshipment, entryReceipt);

        // Romaneios de saída da ORIGEM da carga (não os de um transbordo): é por eles que o
        // contrato de compra é rastreado, e é entre eles que o peso REAL carregado é rateado —
        // mesma leitura de ShipmentLoadsTransshipmentRegisterEntryService.
        var originShipments = await db.Context.StorageTransactions
            .Where(x => x.ShipmentLoadKey == load.Key &&
                        x.TransactionType == StorageTransactionType.SalesShipment &&
                        x.ShipmentLoadTransshipmentKey == null &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .OrderBy(x => x.RowId)
            .ToListAsync();

        if (originShipments.Count == 0)
            throw new ApplicationException(
                $"A carga {load.Code} não tem romaneio de saída de origem.");

        var warehouse = await warehouseService.GetByIdAsync(transshipment.WarehouseCode)
                        ?? throw new ApplicationException(
                            $"Armazém {transshipment.WarehouseCode} não encontrado.");

        try
        {
            await db.BeginTransactionAsync();

            lotExit.ShipmentLoadTransshipmentKey = transshipment.Key;
            lotExit.UpdatedAt = DateTime.Now;
            lotExit.UpdatedBy = userName;

            transshipment.LotExitStorageTransactionKey = lotExit.Key;
            transshipment.UpdatedAt = DateTime.Now;
            transshipment.UpdatedBy = userName;

            // Recalcular e emitir a liberação só DEPOIS do SaveChanges que gravou as FKs.
            await db.SaveChangesAsync();

            await EmitReleasesAsync(
                lotExit, originShipments, warehouse.Code ?? transshipment.WarehouseCode,
                warehouse.Name, userName);

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.TransshipmentLotExitAttached,
                decimal.Zero,
                load.AvailableQuantity,
                $"Saída do lote do transbordo {transshipment.Sequence} vinculada no armazém " +
                $"({transshipment.WarehouseCode}) {transshipment.WarehouseName}: " +
                $"{lotExit.NetWeight:N3}. Romaneio {lotExit.Code}.",
                userName,
                movementContext: new ShipmentLoadMovementContext(
                    WarehouseCode: transshipment.WarehouseCode,
                    WarehouseName: transshipment.WarehouseName,
                    StorageTransactionKey: lotExit.Key));

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        return transshipment;
    }

    /// <summary>
    /// Emite as liberações que devolvem a mercadoria carregada à Expedição de Grãos, uma por
    /// contrato de compra rastreado nos romaneios de saída da origem. Mesma leitura de
    /// <see cref="ShipmentLoadsTransshipmentRegisterEntryService"/>: volume sem contrato
    /// rastreável não falha, degrada para uma nota no <c>Comments</c> do romaneio.
    /// </summary>
    /// <remarks>
    /// A liberação usa <see cref="StorageTransaction.NetWeight"/>, a mesma grandeza que debitou o
    /// LOTE na confirmação deste <c>Shipment</c> (<c>CalculateNetWeight</c>) — não
    /// <see cref="StorageTransaction.GrossWeight"/>. Usar o bruto aqui faria a liberação (e o
    /// crédito simétrico esperado no armazém) carregar um volume maior do que o que de fato saiu
    /// do lote, sobrando peso fantasma sempre que o romaneio tiver desconto de qualidade.
    /// </remarks>
    private async Task EmitReleasesAsync(
        StorageTransaction lotExit,
        IReadOnlyList<StorageTransaction> originShipments,
        string warehouseCode,
        string? warehouseName,
        string userName)
    {
        var shares = ShipmentReleasesFromReturnService.DistributeByWeight(originShipments, lotExit.NetWeight);

        var build = await returnReleases.BuildAsync(
            lotExit, shares, warehouseCode, warehouseName, userName, ReleaseOrigin.Transshipment);

        if (build.Releases.Count > 0)
            db.Context.ShipmentReleases.AddRange(build.Releases);

        if (build.Note is not null)
            lotExit.Comments = AppendComment(lotExit.Comments, build.Note);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Concatena respeitando o VARCHAR(500) da coluna — mesmo precedente de
    /// <see cref="ShipmentLoadsTransshipmentRegisterEntryService"/>.
    /// </summary>
    private static string AppendComment(string? current, string addition)
    {
        var merged = string.IsNullOrWhiteSpace(current) ? addition : $"{current} {addition}";
        return ShipmentLoadTransshipmentRules.Truncate(merged);
    }
}
