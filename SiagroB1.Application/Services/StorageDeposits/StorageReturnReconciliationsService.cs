using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.StorageDeposits;

/// <summary>
/// Reconciliação do retorno de depósito com os romaneios expedidos (spec 2026-10-10 §3.5). Candidato = carregamento
/// da liberação de alguma remessa referenciada pelas linhas do retorno, ainda não reconciliado com outro retorno.
/// Diferença de peso entre nota e romaneios é informativa (D3) — a tela mostra, o serviço não barra.
/// </summary>
public class StorageReturnReconciliationsService(IUnitOfWork db, IConfiguration configuration)
{
    public async Task<List<StorageReturnTransactionDto>> ListAsync(Guid purchaseInvoiceKey)
    {
        EnsureStandalone();
        var releases = await ReleasesOfAsync(purchaseInvoiceKey);
        var rows = new List<StorageReturnTransactionDto>();

        foreach (var (releaseKey, origin) in releases)
        {
            rows.AddRange(await StorageDepositRules.ShippedTransactions(db.Context, releaseKey, origin)
                .Where(t => !db.Context.StorageReturnReconciliations.Any(r => r.StorageTransactionKey == t.Key
                                                                              && r.PurchaseInvoiceKey != purchaseInvoiceKey))
                .Select(t => new StorageReturnTransactionDto
                {
                    Key = t.Key, Code = t.Code, TransactionDate = t.TransactionDate, TruckCode = t.TruckCode,
                    NetWeight = t.NetWeight, UnitOfMeasureCode = t.UnitOfMeasureCode,
                    IsReconciled = db.Context.StorageReturnReconciliations.Any(r => r.StorageTransactionKey == t.Key),
                })
                .AsNoTracking()
                .ToListAsync());
        }

        return rows.OrderBy(r => r.TransactionDate).ThenBy(r => r.Code).ToList();
    }

    public async Task ReconcileAsync(Guid purchaseInvoiceKey, IReadOnlyList<Guid> storageTransactionKeys, string userName)
    {
        EnsureStandalone();

        var status = await db.Context.PurchaseInvoices.AsNoTracking()
                         .Where(x => x.Key == purchaseInvoiceKey).Select(x => (InvoiceStatus?)x.InvoiceStatus).FirstOrDefaultAsync()
                     ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (status == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento de entrada cancelado não pode ser reconciliado.");

        if (storageTransactionKeys.Count == 0)
            throw new DefaultException("Selecione ao menos um romaneio.");

        var available = (await ListAsync(purchaseInvoiceKey)).Where(r => !r.IsReconciled).Select(r => r.Key).ToHashSet();

        foreach (var key in storageTransactionKeys.Distinct())
        {
            if (!available.Contains(key))
            {
                var code = await db.Context.StorageTransactions.Where(t => t.Key == key).Select(t => t.Code).FirstOrDefaultAsync();
                throw new DefaultException(
                    $"O romaneio {code ?? key.ToString()} não é carregamento da liberação da remessa ou já está reconciliado.");
            }

            db.Context.StorageReturnReconciliations.Add(new StorageReturnReconciliation
            {
                PurchaseInvoiceKey = purchaseInvoiceKey, StorageTransactionKey = key,
                CreatedAt = DateTime.Now, CreatedBy = userName,
            });
        }

        await db.SaveChangesAsync();
    }

    public async Task UndoAsync(Guid purchaseInvoiceKey, Guid storageTransactionKey)
    {
        EnsureStandalone();

        var row = await db.Context.StorageReturnReconciliations
                      .FirstOrDefaultAsync(r => r.PurchaseInvoiceKey == purchaseInvoiceKey && r.StorageTransactionKey == storageTransactionKey)
                  ?? throw new NotFoundException("Reconciliação não encontrada.");

        db.Context.StorageReturnReconciliations.Remove(row);
        await db.SaveChangesAsync();
    }

    /// <summary>Cancelar/excluir o retorno solta os romaneios (spec §3.4). Sem SaveChanges: quem chama é dono.</summary>
    public static async Task RemoveForInvoiceAsync(AppDbContext ctx, Guid purchaseInvoiceKey) =>
        ctx.StorageReturnReconciliations.RemoveRange(
            await ctx.StorageReturnReconciliations.Where(r => r.PurchaseInvoiceKey == purchaseInvoiceKey).ToListAsync());

    private async Task<List<(Guid Key, ReleaseOrigin Origin)>> ReleasesOfAsync(Guid purchaseInvoiceKey)
    {
        var releases = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => l.PurchaseInvoiceKey == purchaseInvoiceKey && l.SalesInvoiceItem!.SalesInvoice!.ShipmentReleaseKey != null)
            .Select(l => new { Key = l.SalesInvoiceItem!.SalesInvoice!.ShipmentReleaseKey!.Value, l.SalesInvoiceItem.SalesInvoice.ShipmentRelease!.Origin })
            .Distinct()
            .ToListAsync();

        return releases.Select(r => (r.Key, r.Origin)).ToList();
    }

    private void EnsureStandalone()
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StorageDepositRules.StandaloneOnlyMessage);
    }
}
