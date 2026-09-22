using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Estorna o transbordo da carga (GAC-1181): desfaz a entrada registrada — ou, se a entrada ainda
/// não tiver sido registrada, apenas remove a linha — e devolve o volume à carga.
/// </summary>
/// <remarks>
/// Par de <see cref="ShipmentLoadsTransshipmentRegisterEntryService"/> (Task 5) — aquele registra,
/// este desfaz. Terceiro dos quatro serviços de escrita do módulo; a recusa com destino Transbordo
/// é a Task 8.
/// <para>
/// <b>Tudo numa transação só</b>, e por isso os recálculos e a gravação do log rodam depois do
/// <c>SaveChangesAsync</c> que gravou as mudanças, dentro do mesmo <c>BeginTransactionAsync</c>/
/// <c>CommitAsync</c>. Mesmo precedente de <see cref="ShipmentLoadsTransshipmentRegisterEntryService"/>.
/// </para>
/// <para>
/// <b>APAGA a linha do transbordo</b> (<c>Remove</c>), não a marca como cancelada:
/// <see cref="ShipmentLoadsRecalculateTransshippedService.CalculateTransshippedAsync"/> soma toda
/// linha existente sem filtrar por status — linha existente é transbordo vivo, e uma linha
/// "cancelada" ainda somaria no quarto termo. Quem narra o que aconteceu é o log de movimentação
/// (<see cref="ShipmentLoadMovementType.TransshipmentReversed"/>), como no resto do módulo.
/// </para>
/// <para>
/// ⚠️ <b>O romaneio 15 é cancelado DIRETO</b> (<c>TransactionStatus = Cancelled</c>), sem passar
/// por <c>StorageTransactionsCancelService</c> — aquele serviço BARRA justamente o romaneio de
/// entrada do transbordo (a trava que esta mesma task acrescenta a ele e a
/// <c>StorageTransactionsReverseService</c>): vindo por ali, o cancelamento deixaria o transbordo
/// apontando um romaneio morto enquanto a linha e as liberações continuassem vivas. Aqui a ordem é
/// a inversa — liberação e romaneio primeiro, linha do transbordo por último — e esse estado
/// inconsistente nunca chega a existir.
/// </para>
/// <para>
/// <b>Armazém próprio: <c>Receipt (0)</c> e <c>Shipment (1)</c> NÃO são cancelados</b>, só
/// desvinculados (<c>ShipmentLoadTransshipmentKey = null</c>). São movimento físico pesado na
/// balança — o <c>Receipt</c> pertence à Entrada em Armazenagem e o <c>Shipment</c>, à pesagem que
/// recarregou o caminhão; cada um tem ciclo próprio e é estornado pela tela dele. O que o SISTEMA
/// criou por cima é que se cancela: o crédito do armazém (o <c>TransshipmentReceipt</c>, o 15) e a
/// liberação — sem consumo real, nunca os romaneios de pesagem.
/// </para>
/// <para>
/// ⚠️ <b>O 15 do armazém próprio não é achado por <c>EntryStorageTransactionKey</c></b> — essa
/// chave aponta o <c>Receipt (0)</c>. O 15 é registro À PARTE
/// (<see cref="ShipmentLoadsTransshipmentRegisterEntryService.CreateOwnWarehouseCreditReceiptAsync"/>),
/// achado por <c>ShipmentLoadTransshipmentKey</c> + <c>TransactionType == TransshipmentReceipt</c>.
/// E se a saída do LOTE já tiver sido vinculada (<see cref="ShipmentLoadsTransshipmentAttachLotExitService"/>,
/// Task 4), a liberação a estornar não nasceu do 15 nem do <c>Receipt</c>: nasceu do
/// <c>Shipment (1)</c> apontado por <c>LotExitStorageTransactionKey</c> — é dele, não da entrada,
/// que <c>GeneratedByStorageTransactionKey</c> parte.
/// </para>
/// <para>
/// ⚠️ <b>Este serviço distingue próprio/terceiro pelo TIPO do romaneio de entrada
/// (<c>entry.TransactionType</c>), de propósito — NÃO por <c>WarehouseComplement.IsOwn</c> lido ao
/// vivo.</b> Um transbordo aqui é sempre uma linha JÁ EXISTENTE, e a regra completa (por que
/// criação e linha existente pedem discriminadores diferentes) está documentada uma única vez em
/// <see cref="ShipmentLoadTransshipmentRules"/> — leia lá antes de "padronizar" isto para
/// <c>IsOwn</c> de novo.
/// </para>
/// <para>
/// ⚠️ <b>Transbordo de origem <see cref="TransshipmentOrigin.Refusal"/>: o estorno NÃO desfaz as
/// devoluções das notas</b> que levaram a carga ao transbordo. Elas são documento fiscal com ciclo
/// próprio (retorno de nota, conferência de devolução); o estorno aqui só devolve o saldo à carga
/// — é a assimetria que alguém vai questionar depois, por isso este aviso.
/// </para>
/// </remarks>
public class ShipmentLoadsTransshipmentReverseService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog)
{
    public async Task ExecuteAsync(Guid transshipmentKey, string? reason, string userName)
    {
        var transshipment = await db.Context.ShipmentLoadsTransshipments
                                 .FirstOrDefaultAsync(x => x.Key == transshipmentKey) ??
                             throw new NotFoundException(
                                 $"Shipment load transshipment not found key {transshipmentKey}");

        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == transshipment.ShipmentLoadKey) ??
                   throw new NotFoundException(
                       $"Shipment load not found key {transshipment.ShipmentLoadKey}");

        // TODA a validação antes de qualquer escrita: um estorno recusado não pode deixar efeito
        // no banco.
        var lastSequence = await db.Context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == load.Key)
            .MaxAsync(x => (int?)x.Sequence) ?? 0;

        if (transshipment.Sequence != lastSequence)
            throw new ApplicationException($"Estorne primeiro o transbordo {lastSequence}.");

        List<ShipmentRelease> releases = [];
        StorageTransaction? entry = null;
        StorageTransaction? warehouseCredit = null;
        StorageTransaction? lotExit = null;

        if (transshipment.EntryStorageTransactionKey is { } entryKey)
        {
            entry = await db.Context.StorageTransactions
                        .FirstOrDefaultAsync(x => x.Key == entryKey) ??
                    throw new NotFoundException($"Storage transaction not found key {entryKey}");

            if (entry.TransactionType == StorageTransactionType.TransshipmentReceipt)
            {
                // Terceiro: o 15 É a entrada, e a liberação nasce dele direto.
                releases = await db.Context.ShipmentReleases
                    .Where(x => x.GeneratedByStorageTransactionKey == entryKey)
                    .ToListAsync();
            }
            else
            {
                // Próprio: EntryStorageTransactionKey aponta o Receipt (0) da pesagem, não o
                // crédito do armazém. O 15 é registro À PARTE, achado pela
                // ShipmentLoadTransshipmentKey. Se a saída do LOTE já tiver sido vinculada
                // (Task 4), a liberação nasceu dela — GeneratedByStorageTransactionKey aponta o
                // Shipment (1), não a entrada.
                warehouseCredit = await db.Context.StorageTransactions
                    .FirstOrDefaultAsync(x =>
                        x.ShipmentLoadTransshipmentKey == transshipment.Key &&
                        x.TransactionType == StorageTransactionType.TransshipmentReceipt);

                if (transshipment.LotExitStorageTransactionKey is { } lotExitKey)
                {
                    lotExit = await db.Context.StorageTransactions
                                  .FirstOrDefaultAsync(x => x.Key == lotExitKey) ??
                              throw new NotFoundException(
                                  $"Storage transaction not found key {lotExitKey}");

                    releases = await db.Context.ShipmentReleases
                        .Where(x => x.GeneratedByStorageTransactionKey == lotExitKey)
                        .ToListAsync();
                }
            }

            if (releases.Any(x => x.ShippedQuantity > ShipmentLoadTransshipmentRules.Tolerance))
                throw new ApplicationException(
                    "A liberação do transbordo já foi embarcada. Estorne a Expedição de saída antes.");
        }

        // Este transbordo É o último por Sequence, mas isso não garante por si só que ele ainda
        // esteja ABERTO: uma vez que a Task 7 vincule a saída, HasOpenTransshipmentAsync passa a
        // devolver false para ele mesmo continuando o de maior número. Reverter depois disso
        // desfaria um transbordo que a carga já deixou para trás.
        var isOpen = await ShipmentLoadsRecalculateTransshippedService
            .HasOpenTransshipmentAsync(db.Context, load.Key);

        if (!isOpen)
            throw new ApplicationException(
                $"O transbordo {transshipment.Sequence} já tem a Expedição de venda vinculada e " +
                "não pode ser estornado.");

        try
        {
            await db.BeginTransactionAsync();

            if (entry != null)
            {
                var cancellationReason = ShipmentLoadTransshipmentRules.Truncate(
                    $"Estorno do transbordo {transshipment.Sequence} da carga {load.Code}." +
                    (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Motivo: {reason.Trim()}."));

                if (entry.TransactionType == StorageTransactionType.TransshipmentReceipt)
                {
                    entry.TransactionStatus = StorageTransactionsStatus.Cancelled;
                    entry.UpdatedAt = DateTime.Now;
                    entry.UpdatedBy = userName;

                    foreach (var release in releases)
                    {
                        release.Status = ReleaseStatus.Cancelled;
                        release.CancellationReason = cancellationReason;
                        release.CanceledAt = DateTime.Now;
                        release.CanceledBy = userName;
                        release.UpdatedAt = DateTime.Now;
                        release.UpdatedBy = userName;
                    }
                }
                else
                {
                    // Armazém próprio: Receipt (0) e Shipment (1) são movimento físico pesado na
                    // balança — cada um pertence a um ciclo próprio (Entrada em Armazenagem e
                    // pesagem de recarga) e é estornado pela tela dele. Só desvincula os dois. O
                    // que o SISTEMA criou por cima — o crédito do armazém (o 15) e a liberação —
                    // é o que se cancela.
                    entry.ShipmentLoadTransshipmentKey = null;
                    entry.UpdatedAt = DateTime.Now;
                    entry.UpdatedBy = userName;

                    if (warehouseCredit != null)
                    {
                        warehouseCredit.TransactionStatus = StorageTransactionsStatus.Cancelled;
                        warehouseCredit.UpdatedAt = DateTime.Now;
                        warehouseCredit.UpdatedBy = userName;
                    }

                    if (lotExit != null)
                    {
                        lotExit.ShipmentLoadTransshipmentKey = null;
                        lotExit.UpdatedAt = DateTime.Now;
                        lotExit.UpdatedBy = userName;
                    }

                    foreach (var release in releases)
                    {
                        release.Status = ReleaseStatus.Cancelled;
                        release.CancellationReason = cancellationReason;
                        release.CanceledAt = DateTime.Now;
                        release.CanceledBy = userName;
                        release.UpdatedAt = DateTime.Now;
                        release.UpdatedBy = userName;
                    }
                }
            }

            var sequence = transshipment.Sequence;
            var warehouseCode = transshipment.WarehouseCode;
            var warehouseName = transshipment.WarehouseName;
            var outgoing = transshipment.OutgoingQuantity;
            var entryType = entry?.TransactionType;
            var entryCode = entry?.Code;
            var entryKeyForLog = entry?.Key;
            var warehouseCreditCode = warehouseCredit?.Code;
            var lotExitCode = lotExit?.Code;

            db.Context.ShipmentLoadsTransshipments.Remove(transshipment);

            // Recalcular só DEPOIS do SaveChanges que gravou a remoção — o recálculo consulta o
            // banco e não enxerga o change tracker.
            await db.SaveChangesAsync();

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            var entryNarrative = entryCode switch
            {
                null => string.Empty,
                _ when entryType == StorageTransactionType.TransshipmentReceipt =>
                    $" Romaneio {entryCode} cancelado.",
                _ => $" Entrada em Armazenagem {entryCode} desvinculada." +
                     (warehouseCreditCode != null
                         ? $" Crédito do armazém {warehouseCreditCode} cancelado."
                         : string.Empty) +
                     (lotExitCode != null
                         ? $" Saída do lote {lotExitCode} desvinculada."
                         : string.Empty),
            };

            var description =
                $"Transbordo {sequence} estornado no armazém ({warehouseCode}) {warehouseName}: " +
                $"{outgoing:N3} devolvido à carga.{entryNarrative}" +
                (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" Motivo: {reason.Trim()}.");

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.TransshipmentReversed,
                outgoing,
                load.AvailableQuantity,
                description,
                userName,
                movementContext: new ShipmentLoadMovementContext(
                    WarehouseCode: warehouseCode,
                    WarehouseName: warehouseName,
                    StorageTransactionKey: entryKeyForLog));

            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }
    }
}
