using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Conclui a TRANSFERÊNCIA entre armazéns de terceiros (GAC-1176): a carga descarregou num armazém
/// de terceiro e termina ali — encerra sem faturamento, porque não atendeu contrato de venda.
/// </summary>
/// <remarks>
/// O processo até aqui é o do transbordo (GAC-1181): saída da carga, entrada pesada, quebra,
/// romaneio de crédito no armazém e liberação. O que faltava era o FIM — o transbordo só fechava
/// com uma Expedição de venda vinculada como saída, e a carga que termina no armazém ficava em
/// "Em transbordo" para sempre, sem poder ser concluída nem cancelada.
/// <para>
/// <b>Este serviço não grava o status da carga.</b> Ele grava o FATO
/// (<see cref="ShipmentLoadTransshipment.FinalDestinationAt"/>) e deixa o escritor único
/// (<see cref="ShipmentLoadsRecalculateInvoicedService"/>) derivar a situação — por isso qualquer
/// recálculo posterior ("Recalcular Saldo", por exemplo) mantém a carga concluída, ao contrário de
/// um <c>Completed</c> escrito à mão.
/// </para>
/// <para>
/// <b>O romaneio de entrada e as liberações ficam VIVOS</b>: a mercadoria está disponível no
/// armazém de destino e sai dali depois, pela Expedição de Grãos, em OUTRA carga.
/// </para>
/// <para>
/// ⚠️ <b>Só armazém de terceiro, decidido pela estrutura persistida</b> (a entrada é o
/// <see cref="StorageTransactionType.TransshipmentReceipt"/>), não por <c>IsOwn</c> lido ao vivo —
/// é linha já existente. Ver o <c>remarks</c> de <see cref="ShipmentLoadTransshipmentRules"/>.
/// </para>
/// <para>
/// O transbordo comum concluído aqui VIRA <see cref="TransshipmentType.Transfer"/>: o plano mudou
/// no meio do caminho, e a tela passa a contar a história certa.
/// </para>
/// </remarks>
public class ShipmentLoadsTransshipmentCompleteService(
    IUnitOfWork db,
    ShipmentLoadsMovementLogService movementLog,
    ShipmentLoadsChangeLogService changeLog)
{
    public async Task ExecuteAsync(Guid transshipmentKey, string userName)
    {
        var transshipment = await db.Context.ShipmentLoadsTransshipments
                                 .FirstOrDefaultAsync(x => x.Key == transshipmentKey) ??
                             throw new NotFoundException(
                                 $"Shipment load transshipment not found key {transshipmentKey}");

        var load = await db.Context.ShipmentLoads
                       .FirstOrDefaultAsync(x => x.Key == transshipment.ShipmentLoadKey) ??
                   throw new NotFoundException(
                       $"Shipment load not found key {transshipment.ShipmentLoadKey}");

        // TODA a validação antes de qualquer escrita.
        ShipmentLoadRefusalRules.EnsureNoPendingRefusal(load);

        if (transshipment.FinalDestinationAt != null)
            throw new ApplicationException(
                $"A transferência {transshipment.Sequence} da carga {load.Code} já foi concluída.");

        await ShipmentLoadTransshipmentRules.EnsureIsTheLastOneAsync(db.Context, transshipment, "concluir");

        if (load.Status != ShipmentLoadStatus.InTransshipment)
            throw new ApplicationException(
                $"A carga {load.Code} não está em transbordo.");

        ShipmentLoadTransshipmentRules.EnsureEntryIsRegistered(transshipment);

        var entry = await db.Context.StorageTransactions
                        .FirstOrDefaultAsync(x => x.Key == transshipment.EntryStorageTransactionKey) ??
                    throw new NotFoundException(
                        $"Storage transaction not found key {transshipment.EntryStorageTransactionKey}");

        if (entry.TransactionType != StorageTransactionType.TransshipmentReceipt)
            throw new ApplicationException(
                $"O transbordo {transshipment.Sequence} é em armazém próprio: a mercadoria que " +
                "fica em armazém próprio é registrada pela Entrada em Armazenagem, não pela " +
                "transferência entre armazéns de terceiros.");

        if (await ShipmentLoadsRecalculateTransshippedService.IsClosedAsync(db.Context, transshipmentKey))
            throw new ApplicationException(
                $"O transbordo {transshipment.Sequence} já tem a Expedição de saída vinculada: a " +
                "mercadoria seguiu viagem e não terminou no armazém.");

        try
        {
            await db.BeginTransactionAsync();

            var previousStatus = load.Status;

            transshipment.FinalDestinationAt = DateTime.Now;
            transshipment.FinalDestinationBy = userName;
            transshipment.Type = TransshipmentType.Transfer;
            transshipment.UpdatedAt = DateTime.Now;
            transshipment.UpdatedBy = userName;

            // Recalcular só DEPOIS do SaveChanges que gravou o fato — o recálculo consulta o banco
            // e não enxerga o change tracker.
            await db.SaveChangesAsync();

            await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(
                db.Context, load.Key, excludedInvoiceKeys: null);

            load.UpdatedBy = userName;

            if (load.Status != previousStatus)
            {
                changeLog.Register(
                    load.Key,
                    ShipmentLoadChangeLogFields.Status,
                    ShipmentLoadChangeLogFields.DescribeStatus(previousStatus),
                    ShipmentLoadChangeLogFields.DescribeStatus(load.Status),
                    userName);
            }

            movementLog.Register(
                load.Key,
                ShipmentLoadMovementType.TransferCompleted,
                decimal.Zero,
                load.AvailableQuantity,
                $"Transferência {transshipment.Sequence} concluída no armazém " +
                $"({transshipment.WarehouseCode}) {transshipment.WarehouseName}: entrada " +
                $"{transshipment.EntryQuantity:N3}, quebra {transshipment.ShrinkageQuantity:N3}. " +
                $"Romaneio {entry.Code}. Carga encerrada sem faturamento.",
                userName,
                movementContext: new ShipmentLoadMovementContext(
                    WarehouseCode: transshipment.WarehouseCode,
                    WarehouseName: transshipment.WarehouseName,
                    StorageTransactionKey: entry.Key));

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
