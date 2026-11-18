using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Estorno da recusa CONCLUÍDA, disparado pelo cancelamento da NF-e de uma devolução dela (caso CG000096 da CEAGUI,
/// 18/11/2026: a NF-e de entrada emitida errada não tinha saída). Desfaz os efeitos do destino — romaneio 12 e
/// liberações (Armazém) ou romaneio 18 (Quebra de trajeto) — e devolve a recusa a Pendente, para que a reavaliação
/// de sempre (<see cref="ShipmentLoadRefusalCompleteService.TryCompleteAsync"/>) a cancele (não sobrou devolução)
/// ou a conclua de novo só com as devoluções que continuam valendo.
/// </summary>
/// <remarks>
/// <see cref="EnsureCanReopenAsync"/> é o ensaio que roda ANTES de falar com a SEFAZ: tudo que impediria o estorno
/// precisa recusar ali, porque depois do evento registrado a NF-e está cancelada de qualquer jeito.
/// <para>
/// O destino Transbordo não é desfeito daqui: o transbordo tem ciclo próprio (entrada, saída do lote, liberação) e
/// o estorno dele é do usuário, na Montagem de Carga. Enquanto o transbordo aberto pela recusa existir, o ensaio
/// recusa. ⚠️ Não há chave entre a recusa e o transbordo: ele é achado por <c>Origin = Refusal</c> criado a partir
/// da conclusão da recusa.
/// </para>
/// <para>
/// Roda SEMPRE na transação de quem chama (<c>SalesInvoicesCancelService</c>); só salva, nunca comita.
/// </para>
/// </remarks>
public class ShipmentLoadRefusalReopenService(
    IUnitOfWork db,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ShipmentLoadsMovementLogService movementLog)
{
    private const decimal Tolerance = 0.001m;

    private sealed record Plan(
        ShipmentLoadRefusal Refusal, ShipmentLoad Load, StorageTransaction? Entry, List<ShipmentRelease> Releases);

    /// <summary>Ensaio: recusa o que não pode ser desfeito, sem alterar nada. No-op fora de recusa concluída.</summary>
    public async Task EnsureCanReopenAsync(SalesInvoice returnInvoice) => await BuildPlanAsync(returnInvoice);

    /// <summary>
    /// Desfaz os efeitos da recusa concluída e a devolve a Pendente. No-op fora de recusa concluída.
    /// </summary>
    public async Task ReopenAsync(SalesInvoice returnInvoice, string userName)
    {
        if (await BuildPlanAsync(returnInvoice) is not { } plan)
            return;

        var (refusal, load, entry, releases) = plan;
        var now = DateTime.Now;

        foreach (var release in releases)
        {
            release.Status = ReleaseStatus.Cancelled;
            release.CancellationReason =
                $"Recusa da carga {load.Code} desfeita pelo cancelamento da NF-e da devolução {returnInvoice.InvoiceNumber}.";
            release.CanceledAt = now;
            release.CanceledBy = userName;
            release.UpdatedAt = now;
            release.UpdatedBy = userName;
        }

        if (entry is not null)
        {
            // Cancelado à mão: StorageTransactionsCancelService recusa o romaneio de recusa de propósito.
            entry.TransactionStatus = StorageTransactionsStatus.Cancelled;
            entry.CanceledAt = now;
            entry.CanceledBy = userName;
            entry.UpdatedAt = now;
            entry.UpdatedBy = userName;
            entry.Comments = Truncate(
                $"{entry.Comments} NF-e da devolução {returnInvoice.InvoiceNumber} cancelada por {userName} em " +
                $"{now:dd/MM/yyyy HH:mm}: recusa desfeita.");
        }

        // De volta a Pendente: a carga fica travada até a reavaliação no fim do cancelamento, na mesma transação.
        refusal.Status = ShipmentLoadRefusalStatus.Pending;
        refusal.CompletedAt = null;
        refusal.CompletedBy = null;

        // Os termos do saldo e o sincronizador do contrato leem o banco.
        await db.SaveChangesAsync();

        // GAC-1214: o romaneio 12 cancelado deixa de devolver saldo ao contrato de compra.
        if (entry is { TransactionType: StorageTransactionType.SalesShipmentReturn })
            await PurchaseContractsReturnReleaseAllocationSync.SyncByGeneratorAsync(db.Context, entry.Key);

        await ShipmentLoadsRecalculateInvoicedService.RecalculateAsync(db.Context, load.Key, excludedInvoiceKeys: null);

        movementLog.Register(
            load.Key,
            ShipmentLoadMovementType.RefusalCancelled,
            entry?.NetWeight ?? decimal.Zero,
            load.AvailableQuantity,
            $"Recusa concluída desfeita pelo cancelamento da NF-e da devolução {returnInvoice.InvoiceNumber}." +
            (entry is null ? string.Empty : $" Romaneio {entry.Code} cancelado ({entry.NetWeight:N3})."),
            userName,
            movementContext: entry is null ? null : new ShipmentLoadMovementContext(StorageTransactionKey: entry.Key));

        load.UpdatedAt = now;
        load.UpdatedBy = userName;
        await db.SaveChangesAsync();
    }

    private async Task<Plan?> BuildPlanAsync(SalesInvoice returnInvoice)
    {
        if (returnInvoice.ShipmentLoadRefusalKey is not { } refusalKey)
            return null;

        var refusal = await db.Context.ShipmentLoadRefusals.FirstOrDefaultAsync(x => x.Key == refusalKey);
        if (refusal is not { Status: ShipmentLoadRefusalStatus.Completed })
            return null;

        var load = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == refusal.ShipmentLoadKey);

        // Índice único: uma recusa Pendente por carga. Voltar esta a Pendente colidiria com a outra.
        if (await db.Context.ShipmentLoadRefusals.AnyAsync(x =>
                x.ShipmentLoadKey == load.Key && x.Key != refusalKey && x.Status == ShipmentLoadRefusalStatus.Pending))
            throw new DefaultException(
                $"A carga {load.Code} tem outra recusa aguardando NF-e: conclua ou cancele essa recusa antes de " +
                "cancelar a NF-e desta devolução.");

        if (refusal.Destination == RefusalDestination.Transshipment)
            await EnsureTransshipmentReversedAsync(refusal, load);

        var entry = await ResolveEntryAsync(refusal, load, returnInvoice);
        List<ShipmentRelease> releases = [];

        if (entry is { TransactionType: StorageTransactionType.SalesShipmentReturn })
        {
            releases = await db.Context.ShipmentReleases
                .Where(x => x.GeneratedByStorageTransactionKey == entry.Key && x.Status != ReleaseStatus.Cancelled)
                .ToListAsync();

            // Antes do saldo do armazém: a mensagem acionável é "estorne o embarque", não "saldo curto".
            foreach (var release in releases)
            {
                var shipped = await recalcShipped.CalculateShippedAsync(release.Key, release.Origin);
                if (shipped > decimal.Zero)
                    throw new DefaultException(
                        $"A mercadoria devolvida pelo romaneio {entry.Code} já foi embarcada de novo " +
                        $"({shipped:N3} de {release.ReleasedQuantity:N3}, pela liberação {release.DocNumber}). " +
                        "Estorne esse embarque antes de cancelar a NF-e da devolução.");
            }

            var warehouseBalance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
                db.Context, entry.WarehouseCode, entry.ItemCode);

            if (warehouseBalance < entry.NetWeight)
                throw new DefaultException(
                    $"A mercadoria da carga {load.Code} foi devolvida ao armazém {entry.WarehouseCode} pelo romaneio " +
                    $"{entry.Code}. Saldo do armazém hoje ({warehouseBalance:N3}) menor que a quantidade a debitar de " +
                    $"volta ({entry.NetWeight:N3}) — parte já foi consumida por outra operação. " +
                    "A NF-e da devolução não pode ser cancelada.");
        }

        // Sem romaneio (Refaturamento, Transbordo já estornado), o volume devolvido voltou ao saldo da carga e a
        // origem volta a consumi-lo. Com romaneio o efeito é neutro: ele sai e a origem volta, na mesma quantidade.
        if (entry is null)
        {
            var quantity = await db.Context.SalesInvoicesItems
                .Where(i => i.SalesInvoiceKey == returnInvoice.Key)
                .SumAsync(i => (decimal?)i.Quantity) ?? decimal.Zero;

            if (returnInvoice.InvoiceStatus == InvoiceStatus.Confirmed && load.AvailableQuantity < quantity - Tolerance)
                throw new DefaultException(
                    $"O volume desta devolução já foi faturado de novo na carga {load.Code} (saldo a faturar " +
                    $"{load.AvailableQuantity:N3}, devolução {quantity:N3}): cancele o novo documento de saída antes de " +
                    "cancelar a NF-e da devolução.");
        }

        return new Plan(refusal, load, entry, releases);
    }

    private async Task EnsureTransshipmentReversedAsync(ShipmentLoadRefusal refusal, ShipmentLoad load)
    {
        // CompletedAt é carimbado logo antes dos efeitos; a folga cobre relógio e arredondamento do datetime.
        var since = (refusal.CompletedAt ?? refusal.CreatedAt).AddMinutes(-1);

        var sequence = await db.Context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == load.Key && x.Origin == TransshipmentOrigin.Refusal && x.CreatedAt >= since)
            .OrderBy(x => x.Sequence)
            .Select(x => (int?)x.Sequence)
            .FirstOrDefaultAsync();

        if (sequence is not null)
            throw new DefaultException(
                $"A recusa da carga {load.Code} abriu o transbordo {sequence}: estorne o transbordo na Montagem de " +
                "Carga antes de cancelar a NF-e da devolução.");
    }

    /// <summary>
    /// O romaneio (12 ou 18) que a conclusão gerou. Pelo vínculo da devolução; no dado anterior ao vínculo
    /// (GAC-1213), o único romaneio vivo da carga, daquele tipo, que nenhuma devolução aponta.
    /// </summary>
    private async Task<StorageTransaction?> ResolveEntryAsync(
        ShipmentLoadRefusal refusal, ShipmentLoad load, SalesInvoice returnInvoice)
    {
        StorageTransactionType? type = refusal.Destination switch
        {
            RefusalDestination.Warehouse => StorageTransactionType.SalesShipmentReturn,
            RefusalDestination.TransitLoss => StorageTransactionType.TransitLoss,
            _ => null,
        };

        if (type is null)
            return null;

        if (returnInvoice.RefusalStorageTransactionKey is { } entryKey)
            return await db.Context.StorageTransactions.FirstOrDefaultAsync(x =>
                x.Key == entryKey && x.TransactionStatus != StorageTransactionsStatus.Cancelled);

        var linkedKeys = db.Context.SalesInvoices
            .Where(x => x.RefusalStorageTransactionKey != null)
            .Select(x => x.RefusalStorageTransactionKey);

        var candidates = await db.Context.StorageTransactions
            .Where(x => x.RefusedFromShipmentLoadKey == load.Key &&
                        x.TransactionType == type &&
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                        !linkedKeys.Contains(x.Key))
            .ToListAsync();

        return candidates.Count switch
        {
            0 => null,
            1 => candidates[0],
            _ => throw new DefaultException(
                $"A carga {load.Code} tem mais de um romaneio de recusa sem vínculo com a devolução " +
                $"({string.Join(", ", candidates.Select(x => x.Code))}): não é possível saber qual desfazer. " +
                "Acione o suporte."),
        };
    }

    /// <summary>Corta no VARCHAR(500) da coluna <c>Comments</c>.</summary>
    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];
}
