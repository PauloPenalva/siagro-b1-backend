using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesCancelService(
    IUnitOfWork db,
    SalesShipmentReleasesRecalculateShippedService recalcShipped,
    SalesContractsAllocationDeleteForInvoiceService allocationDelete,
    ShipmentLoadsBalanceHookService loadHook,
    ILogger<SalesInvoicesCancelService> logger,
    ShipmentLoadRefusalCompleteService? refusalComplete = null)
{
    public Task ExecuteAsync(Guid key, string userName) => CancelAsync(key, userName, afterNfe: false);

    /// <summary>Fase 2 do cancelamento da NF-e: a SEFAZ já cancelou; a trava da NF-e não se aplica.</summary>
    public virtual Task CancelAfterNfeAsync(Guid key, string userName) => CancelAsync(key, userName, afterNfe: true);

    /// <summary>Cancelamento da devolução pendente pela recusa de carga (spec 2026-10-09 §5.4), na transação dela.</summary>
    public Task CancelForRefusalAsync(Guid key, string userName) =>
        CancelAsync(key, userName, afterNfe: false, CommitMode.Deferred, fromRefusal: true);

    /// <summary>Ensaio antes de falar com a SEFAZ: só as regras de negócio, nada é alterado.</summary>
    public async Task EnsureCanCancelAsync(Guid key)
    {
        var invoice = await db.Context.SalesInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de saída não encontrado.");

        EnsureBusinessRules(invoice);
    }

    private async Task CancelAsync(Guid key, string userName, bool afterNfe,
        CommitMode commitMode = CommitMode.Auto, bool fromRefusal = false)
    {
        var existingInvoice = await db.Context.SalesInvoices
                                  .Include(e => e.SalesTransactions)
                                  .FirstOrDefaultAsync(x => x.Key == key) ??
                                    throw new KeyNotFoundException($"Key {key} not found");

        if (existingInvoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento já está cancelado.");

        if (afterNfe)
        {
            if (existingInvoice.NfeStatus != NfeStatus.Cancelled)
                throw new DefaultException("A NF-e deste documento não está cancelada na SEFAZ.");
        }
        else
        {
            SalesInvoiceNfeLock.EnsureCancellable(existingInvoice);
        }

        EnsureBusinessRules(existingInvoice);

        // Pós-SEFAZ (2b) passa: a NF-e já foi cancelada e o documento precisa acompanhar (Review Focus 3).
        if (!afterNfe && !fromRefusal)
            await SalesInvoicesRefusalLink.EnsureNotInPendingRefusalAsync(db.Context, existingInvoice);

        var salesTransactionsKeys = existingInvoice.SalesTransactions?.Select(x => x.Key)
            .ToList() ?? [];
        
        existingInvoice.SalesTransactions?.Clear();

        // Liberações de venda afetadas: coletar antes de limpar a chave para recalcular depois.
        var affectedReleaseKeys = new HashSet<Guid>();

        try
        {
            if (commitMode == CommitMode.Auto)
                await db.BeginTransactionAsync();

            foreach (var salesTransactionsKey in salesTransactionsKeys)
            {
                var salesTransaction = await db.Context.StorageTransactions
                    .FirstOrDefaultAsync(x => x.Key == salesTransactionsKey);

                if (salesTransaction != null)
                {
                    if (salesTransaction.SalesShipmentReleaseKey is { } releaseKey)
                        affectedReleaseKeys.Add(releaseKey);

                    salesTransaction.TransactionStatus = StorageTransactionsStatus.Confirmed;
                    salesTransaction.InvoiceQty = 0;
                    salesTransaction.InvoiceSerie = string.Empty;
                    salesTransaction.InvoiceNumber = string.Empty;
                    salesTransaction.UpdatedAt = DateTime.Now;
                    salesTransaction.UpdatedBy = userName;
                    salesTransaction.SalesInvoiceKey = null;
                    salesTransaction.SalesShipmentReleaseKey = null;

                    await db.SaveChangesAsync();
                }
            }

            existingInvoice.InvoiceStatus = InvoiceStatus.Cancelled;
            existingInvoice.CanceledAt = DateTime.Now;
            existingInvoice.CanceledBy = userName;

            // Ledger: remove as alocações da nota (inclui pares de realocação) e recalcula
            // contratos e liberações derivado-da-soma, na mesma transação.
            await allocationDelete.ExecuteAsync(key, userName, CommitMode.Deferred);

            // Cancelar um RETORNO faz o documento deixar de valer: a origem volta a
            // "Confirmada" e a entrega reabre. Depois do allocationDelete, para que o
            // recálculo da restauração seja o último a rodar. No-op para documento normal.
            await SalesInvoicesReturnOriginRestoreService.ExecuteAsync(
                db.Context, existingInvoice, userName);

            // A devolução cancelada deixa de contar na quantidade devolvida da origem. Só a NF-e
            // própria CONFIRMADA chega aqui com valor a tirar (cancelamento da NF-e). Depois da troca
            // de status, que o serviço lê em memória, e antes do SaveChanges que a persiste.
            if (existingInvoice.InvoiceType == SalesInvoiceType.Return)
                await SalesInvoicesRecalculateReturnedService.RecalculateAsync(
                    db.Context, existingInvoice.SalesInvoiceOriginKey);

            await db.SaveChangesAsync();

            // Romaneios voltaram a Confirmed e perderam a chave → o saldo liberado é restaurado.
            // No fluxo da CARGA a coleção nasce vazia e este laço é no-op: allocationDelete já
            // recalcula contratos E liberações a partir do ledger. Não é esquecimento.
            foreach (var releaseKey in affectedReleaseKeys)
                await recalcShipped.RecalculateAsync(releaseKey);

            // Saldo da carga. Vale para os DOIS tipos: cancelar uma nota normal devolve saldo,
            // cancelar uma devolução volta a consumi-lo — o gancho deriva o sinal do delta.
            await loadHook.ApplyAsync(
                existingInvoice,
                ShipmentLoadMovementType.BillingCancelled,
                userName,
                $"Documento de saída {existingInvoice.InvoiceNumber} cancelado.");

            await db.SaveChangesAsync();

            // Spec 2026-10-09: o 2b cancelou a NF-e de uma devolução de recusa pendente. Sem reavaliar aqui, nada
            // concluiria (as demais já confirmadas) nem cancelaria (era a única viva) a recusa — carga travada para
            // sempre. DEPOIS do cancelamento gravado: a reavaliação lê as devoluções vivas do banco. Mesma transação.
            if (afterNfe && refusalComplete is not null && existingInvoice.ShipmentLoadRefusalKey is { } refusalKey)
            {
                await refusalComplete.TryCompleteAsync(refusalKey, userName);
                await db.SaveChangesAsync();
            }

            if (commitMode == CommitMode.Auto)
                await db.CommitAsync();
        }
        catch (Exception e)
        {
            // Diferido: quem abriu a transação desfaz; a exceção sobe sem embrulho.
            if (commitMode != CommitMode.Auto)
                throw;

            await db.RollbackAsync();
            logger.LogError(e.Message);
            throw new ApplicationException(e.Message);
        }
        
    }

    private void EnsureBusinessRules(SalesInvoice invoice)
    {
        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento já está cancelado.");

        // A devolução com NF-e confirmada só sai pelo cancelamento da NF-e — que é justamente o caminho afterNfe.
        if (invoice.InvoiceType == SalesInvoiceType.Return && invoice.InvoiceStatus == InvoiceStatus.Confirmed
            && !invoice.IsNfeReturn)
            throw new DefaultException("Documento do tipo retorno já está confirmado. Não é possivel cancelar.");

        if (HasReturn(invoice))
            throw new DefaultException("Documento de saída possui retorno.");
    }

    private bool HasReturn(SalesInvoice salesInvoice)
    {
        return db.Context.SalesInvoices.Any(x => x.SalesInvoiceOriginKey == salesInvoice.Key &&
                                                 x.InvoiceType == SalesInvoiceType.Return && x.InvoiceStatus != InvoiceStatus.Cancelled);
    }
}