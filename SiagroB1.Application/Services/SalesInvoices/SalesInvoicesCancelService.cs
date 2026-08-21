using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Enums;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesCancelService(
    IUnitOfWork db,
    SalesShipmentReleasesRecalculateShippedService recalcShipped,
    SalesContractsAllocationDeleteForInvoiceService allocationDelete,
    ShipmentLoadsBalanceHookService loadHook,
    ILogger<SalesInvoicesCancelService> logger)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var existingInvoice = await db.Context.SalesInvoices
                                  .Include(e => e.SalesTransactions)
                                  .FirstOrDefaultAsync(x => x.Key == key) ??
                                    throw new KeyNotFoundException($"Key {key} not found");

        if (existingInvoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new ApplicationException("Documento já está cancelado.");
        
        if (existingInvoice.InvoiceType == SalesInvoiceType.Return && existingInvoice.InvoiceStatus == InvoiceStatus.Confirmed)
            throw new ApplicationException("Documento do tipo retorno já está confirmado. Não é possivel cancelar.");
        
        if (HasReturn(existingInvoice))
            throw  new ApplicationException("Documento de saída possui retorno.");
        
        var salesTransactionsKeys = existingInvoice.SalesTransactions?.Select(x => x.Key)
            .ToList() ?? [];
        
        existingInvoice.SalesTransactions?.Clear();

        // Liberações de venda afetadas: coletar antes de limpar a chave para recalcular depois.
        var affectedReleaseKeys = new HashSet<Guid>();

        try
        {
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

            // Ledger: remove as alocações da nota (inclui pares de realocação) e recalcula
            // contratos e liberações derivado-da-soma, na mesma transação.
            await allocationDelete.ExecuteAsync(key, userName, CommitMode.Deferred);

            // Cancelar um RETORNO faz o documento deixar de valer: a origem volta a
            // "Confirmada" e a entrega reabre. Depois do allocationDelete, para que o
            // recálculo da restauração seja o último a rodar. No-op para documento normal.
            await SalesInvoicesReturnOriginRestoreService.ExecuteAsync(
                db.Context, existingInvoice, userName);

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

            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            logger.LogError(e.Message);
            throw new ApplicationException(e.Message);
        }
        
    }

    private bool HasReturn(SalesInvoice salesInvoice)
    {
        return db.Context.SalesInvoices.Any(x => x.SalesInvoiceOriginKey == salesInvoice.Key &&
                                                 x.InvoiceType == SalesInvoiceType.Return && x.InvoiceStatus != InvoiceStatus.Cancelled);
    }
}