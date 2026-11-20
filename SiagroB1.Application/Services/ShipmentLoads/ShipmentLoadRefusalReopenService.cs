using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// O que o cancelamento da NF-e fez com a recusa concluída. <see cref="KeptReason"/> preenchido = os efeitos do
/// destino ficaram de pé e a devolução cancelada precisa ser SUBSTITUÍDA
/// (<see cref="ShipmentLoadRefusalReopenService.ReplaceReturnAsync"/>).
/// </summary>
public sealed record RefusalReopenOutcome(Guid LoadKey, Guid? EntryKey, string? KeptReason)
{
    public bool KeepsEffects => KeptReason is not null;
}

/// <summary>
/// Cancelamento da NF-e de uma devolução de recusa CONCLUÍDA (caso CG000096 da CEAGUI, 18/11/2026: a NF-e de
/// entrada emitida errada não tinha saída). Dois desfechos:
/// <list type="bullet">
/// <item><b>Desfaz</b> — enquanto a mercadoria devolvida está onde a recusa a deixou: cancela o romaneio 12 e as
/// liberações (Armazém) ou o romaneio 18 (Quebra de trajeto) e devolve a recusa a Pendente, para que a reavaliação de
/// sempre (<see cref="ShipmentLoadRefusalCompleteService.TryCompleteAsync"/>) a cancele (não sobrou devolução) ou a
/// conclua de novo só com as devoluções que continuam valendo.</item>
/// <item><b>Substitui</b> — quando o físico já andou (mercadoria reembarcada, saldo do armazém consumido, volume
/// refaturado, transbordo da recusa de pé): nada é desfeito, a recusa continua Concluída e nasce uma devolução
/// Pendente igual à cancelada, com os mesmos vínculos, para o usuário corrigir e transmitir. O erro era só do
/// documento fiscal; travar o cancelamento deixava o usuário sem saída (20/11/2026).</item>
/// </list>
/// </summary>
/// <remarks>
/// <see cref="EnsureCanReopenAsync"/> é o ensaio que roda ANTES de falar com a SEFAZ: tudo que impediria o desfecho
/// precisa recusar ali, porque depois do evento registrado a NF-e está cancelada de qualquer jeito.
/// <para>
/// ⚠️ Não há chave entre a recusa e o transbordo: ele é achado por <c>Origin = Refusal</c> criado a partir da
/// conclusão da recusa.
/// </para>
/// <para>
/// ⚠️ Entre o cancelamento e a autorização da devolução substituta, a origem volta a valer com o romaneio de pé: o
/// saldo da carga fica negativo nesse intervalo, de propósito — ele fecha quando a substituta é confirmada.
/// </para>
/// <para>
/// Roda SEMPRE na transação de quem chama (<c>SalesInvoicesCancelService</c>); só salva, nunca comita.
/// </para>
/// </remarks>
public class ShipmentLoadRefusalReopenService(
    IUnitOfWork db,
    ShipmentReleasesRecalculateShippedService recalcShipped,
    ShipmentLoadsMovementLogService movementLog,
    SalesInvoiceNfeReturnBuilder? nfeReturnBuilder = null,
    SalesInvoicesCreateService? createService = null)
{
    private const decimal Tolerance = 0.001m;

    private sealed record Plan(
        ShipmentLoadRefusal Refusal, ShipmentLoad Load, StorageTransaction? Entry, List<ShipmentRelease> Releases,
        string? KeptReason);

    /// <summary>Ensaio: recusa o que não pode ser desfeito nem substituído, sem alterar nada. No-op fora de recusa concluída.</summary>
    public async Task EnsureCanReopenAsync(SalesInvoice returnInvoice)
    {
        if (await BuildPlanAsync(returnInvoice) is { KeptReason: not null })
            await BuildReplacementAsync(returnInvoice, "ensaio");
    }

    /// <summary>
    /// Desfaz os efeitos da recusa concluída e a devolve a Pendente — ou, quando o físico já andou, não mexe em nada e
    /// avisa que a devolução precisa ser substituída. <c>null</c> fora de recusa concluída.
    /// </summary>
    public async Task<RefusalReopenOutcome?> ReopenAsync(SalesInvoice returnInvoice, string userName)
    {
        if (await BuildPlanAsync(returnInvoice) is not { } plan)
            return null;

        var (refusal, load, entry, releases, keptReason) = plan;

        if (keptReason is not null)
            return new RefusalReopenOutcome(load.Key, entry?.Key, keptReason);

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

        return new RefusalReopenOutcome(load.Key, entry?.Key, null);
    }

    /// <summary>
    /// Depois do cancelamento gravado (a origem já voltou a valer): cria a devolução Pendente que substitui a
    /// cancelada, com os vínculos da recusa e do romaneio. A recusa continua Concluída — a confirmação da substituta
    /// não reaplica efeito nenhum (<see cref="ShipmentLoadRefusalCompleteService"/> só age em recusa Pendente).
    /// </summary>
    public async Task<SalesInvoice> ReplaceReturnAsync(
        SalesInvoice cancelledReturn, RefusalReopenOutcome outcome, string userName)
    {
        var create = createService ?? throw new InvalidOperationException("SalesInvoicesCreateService não registrado.");
        var replacement = await BuildReplacementAsync(cancelledReturn, userName);

        await create.ExecuteAsync(replacement, userName, CommitMode.Deferred, nfeReturn: true);

        // DEPOIS da criação: ela zera os vínculos vindos do corpo (SalesInvoiceNfeLock.ResetIssuanceFields).
        replacement.ShipmentLoadRefusalKey = cancelledReturn.ShipmentLoadRefusalKey;
        replacement.RefusalStorageTransactionKey = cancelledReturn.RefusalStorageTransactionKey ?? outcome.EntryKey;
        await db.SaveChangesAsync();

        var load = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == outcome.LoadKey);

        movementLog.Register(
            load.Key,
            ShipmentLoadMovementType.Refused,
            decimal.Zero,
            load.AvailableQuantity,
            $"NF-e da devolução {cancelledReturn.InvoiceNumber} cancelada com a recusa mantida ({outcome.KeptReason}). " +
            $"Devolução {replacement.InvoiceNumber} criada para substituí-la, aguardando NF-e de entrada.",
            userName,
            replacement.Key,
            replacement.InvoiceNumber,
            ShipmentLoadMovementContext.FromInvoice(replacement, null));

        load.UpdatedAt = DateTime.Now;
        load.UpdatedBy = userName;
        await db.SaveChangesAsync();

        return replacement;
    }

    /// <summary>Monta (sem gravar) a devolução substituta: mesma origem, mesmas quantidades por item.</summary>
    private async Task<SalesInvoice> BuildReplacementAsync(SalesInvoice cancelledReturn, string userName)
    {
        var builder = nfeReturnBuilder
                      ?? throw new InvalidOperationException("SalesInvoiceNfeReturnBuilder não registrado.");

        var origin = await db.Context.SalesInvoices
                         .Include(i => i.Items)
                         .FirstOrDefaultAsync(i => i.Key == cancelledReturn.SalesInvoiceOriginKey)
                     ?? throw new DefaultException(
                         $"A devolução {cancelledReturn.InvoiceNumber} está sem o documento de origem. Acione o suporte.");

        var quantities = (await db.Context.SalesInvoicesItems.AsNoTracking()
                .Where(i => i.SalesInvoiceKey == cancelledReturn.Key && i.SalesInvoiceItemOriginKey != null)
                .Select(i => new { Key = i.SalesInvoiceItemOriginKey!.Value, i.Quantity })
                .ToListAsync())
            .GroupBy(i => i.Key)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        if (quantities.Count == 0)
            throw new DefaultException(
                $"A devolução {cancelledReturn.InvoiceNumber} está sem itens ligados à origem. Acione o suporte.");

        return await builder.BuildAsync(origin, quantities, cancelledReturn.Comments ?? string.Empty, userName);
    }

    private async Task<Plan?> BuildPlanAsync(SalesInvoice returnInvoice)
    {
        if (returnInvoice.ShipmentLoadRefusalKey is not { } refusalKey)
            return null;

        var refusal = await db.Context.ShipmentLoadRefusals.FirstOrDefaultAsync(x => x.Key == refusalKey);
        if (refusal is not { Status: ShipmentLoadRefusalStatus.Completed })
            return null;

        var load = await db.Context.ShipmentLoads.FirstAsync(x => x.Key == refusal.ShipmentLoadKey);

        var entry = await ResolveEntryAsync(refusal, load, returnInvoice);
        List<ShipmentRelease> releases = [];

        var keptReason = refusal.Destination == RefusalDestination.Transshipment
            ? await TransshipmentKeptReasonAsync(refusal, load)
            : null;

        if (keptReason is null && entry is { TransactionType: StorageTransactionType.SalesShipmentReturn })
        {
            releases = await db.Context.ShipmentReleases
                .Where(x => x.GeneratedByStorageTransactionKey == entry.Key && x.Status != ReleaseStatus.Cancelled)
                .ToListAsync();

            foreach (var release in releases)
            {
                if (await recalcShipped.CalculateShippedAsync(release.Key, release.Origin) > decimal.Zero)
                {
                    keptReason = $"a mercadoria devolvida pelo romaneio {entry.Code} já foi embarcada de novo";
                    break;
                }
            }

            if (keptReason is null)
            {
                var warehouseBalance = await StorageTransactionsWarehouseBalanceService.CalculateAsync(
                    db.Context, entry.WarehouseCode, entry.ItemCode);

                if (warehouseBalance < entry.NetWeight)
                    keptReason =
                        $"o armazém {entry.WarehouseCode} já não tem o saldo devolvido pelo romaneio {entry.Code}";
            }
        }

        // Sem romaneio (Refaturamento, Transbordo já estornado), o volume devolvido voltou ao saldo da carga e a
        // origem volta a consumi-lo. Com romaneio o efeito é neutro: ele sai e a origem volta, na mesma quantidade.
        if (keptReason is null && entry is null)
        {
            var quantity = await db.Context.SalesInvoicesItems
                .Where(i => i.SalesInvoiceKey == returnInvoice.Key)
                .SumAsync(i => (decimal?)i.Quantity) ?? decimal.Zero;

            if (returnInvoice.InvoiceStatus == InvoiceStatus.Confirmed && load.AvailableQuantity < quantity - Tolerance)
                keptReason = $"o volume devolvido já foi faturado de novo na carga {load.Code}";
        }

        // Índice único: uma recusa Pendente por carga. Voltar esta a Pendente colidiria com a outra. Na substituição
        // a recusa continua Concluída e não há colisão.
        if (keptReason is null && await db.Context.ShipmentLoadRefusals.AnyAsync(x =>
                x.ShipmentLoadKey == load.Key && x.Key != refusalKey && x.Status == ShipmentLoadRefusalStatus.Pending))
            throw new DefaultException(
                $"A carga {load.Code} tem outra recusa aguardando NF-e: conclua ou cancele essa recusa antes de " +
                "cancelar a NF-e desta devolução.");

        return new Plan(refusal, load, entry, releases, keptReason);
    }

    private async Task<string?> TransshipmentKeptReasonAsync(ShipmentLoadRefusal refusal, ShipmentLoad load)
    {
        // CompletedAt é carimbado logo antes dos efeitos; a folga cobre relógio e arredondamento do datetime.
        var since = (refusal.CompletedAt ?? refusal.CreatedAt).AddMinutes(-1);

        var sequence = await db.Context.ShipmentLoadsTransshipments
            .Where(x => x.ShipmentLoadKey == load.Key && x.Origin == TransshipmentOrigin.Refusal && x.CreatedAt >= since)
            .OrderBy(x => x.Sequence)
            .Select(x => (int?)x.Sequence)
            .FirstOrDefaultAsync();

        return sequence is null ? null : $"o transbordo {sequence} aberto pela recusa continua de pé";
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
