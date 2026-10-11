using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageDeposits;

/// <summary>Remetido / Retornado / A retornar / Expedido sem retorno da liberação (spec 2026-10-10 §2.5, §5.1).</summary>
public class ShipmentReleasesStorageRemittanceSummaryService(IUnitOfWork db, IConfiguration configuration)
{
    public async Task<StorageRemittanceSummaryDto> ExecuteAsync(Guid releaseKey)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(StorageDepositRules.StandaloneOnlyMessage);

        var release = await db.Context.ShipmentReleases.AsNoTracking().FirstOrDefaultAsync(r => r.Key == releaseKey)
                      ?? throw new NotFoundException("Liberação não encontrada.");

        var summary = new StorageRemittanceSummaryDto();
        var remittance = await StorageDepositRules.FindActiveRemittanceAsync(db.Context, releaseKey);

        if (remittance != null)
        {
            // Remessa editada para ≠1 linha não pode virar 500: soma as linhas.
            var itemKeys = remittance.Items.Where(i => i.Key != null).Select(i => i.Key!.Value).ToList();
            summary.RemittanceKey = remittance.Key;
            summary.RemittanceNumber = StorageDepositRules.DocumentNumber(remittance.TaxDocumentNumber, remittance.InvoiceNumber);
            summary.RemittanceStatus = remittance.InvoiceStatus;
            summary.RemittanceNfeStatus = remittance.NfeStatus;
            summary.RemittedQuantity = remittance.Items.Sum(i => i.Quantity);
            summary.Returns = itemKeys.Count == 0
                ? []
                : await StorageDepositRules.ReturnsOfRemittanceItems(db.Context, itemKeys).AsNoTracking().ToListAsync();
            summary.ReturnedQuantity = summary.Returns.Sum(r => r.Quantity);
            summary.PendingReturnQuantity = summary.RemittedQuantity - summary.ReturnedQuantity;
        }

        summary.Transactions = await StorageDepositRules.TransactionsWithReturn(db.Context, releaseKey, release.Origin)
            .AsNoTracking()
            .OrderBy(t => t.TransactionDate)
            .ToListAsync();
        summary.ShippedWithoutReturnQuantity = summary.Transactions
            .Where(t => t.ReturnPurchaseInvoiceKey == null).Sum(t => t.NetWeight);

        return summary;
    }
}
