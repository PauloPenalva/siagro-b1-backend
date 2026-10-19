using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShippingTransactions;

/// <summary>
/// Trava da troca de liberação (GAC-1177) contra a Conferência de Saldo de Armazém (GAC-1164).
/// </summary>
/// <remarks>
/// A troca grava estorno e Expedição nova com a data da CARGA — retroativos. Uma conferência
/// <see cref="WarehouseReconciliationStatus.Approved"/> com data de referência igual ou posterior
/// já gerou a Perda/Sobra sobre o saldo daquela data; mexer no saldo por baixo dela deixaria o
/// romaneio de ajuste errado sem nada indicando. Por isso recusa e pede o cancelamento da
/// conferência, que é o caminho de estorno dela.
/// A troca de fornecedor/armazém do romaneio de compra usa a mesma trava, com a data do romaneio
/// (<c>dateLabel</c>/<c>action</c> só mudam a frase; o padrão é a da troca de liberação).
/// </remarks>
public class ShippingReleaseChangeReconciliationGuard(AppDbContext context)
{
    public async Task EnsureNoApprovedReconciliationAsync(
        string itemCode,
        IEnumerable<string> warehouseCodes,
        DateTime referenceDate,
        string dateLabel = "data da carga",
        string action = "trocar a liberação")
    {
        var warehouses = warehouseCodes
            .Where(x => !string.IsNullOrEmpty(x))
            .Distinct()
            .ToList();

        if (warehouses.Count == 0)
            return;

        var limit = referenceDate.Date;

        var reconciliation = await context.WarehouseReconciliations
            .AsNoTracking()
            .Where(x => x.Status == WarehouseReconciliationStatus.Approved &&
                        x.ItemCode == itemCode &&
                        warehouses.Contains(x.WarehouseCode) &&
                        x.ReferenceDate >= limit)
            .OrderBy(x => x.ReferenceDate)
            .Select(x => new { x.Code, x.WarehouseCode, x.ReferenceDate })
            .FirstOrDefaultAsync();

        if (reconciliation != null)
            throw new ApplicationException(
                $"A conferência de saldo {reconciliation.Code} do armazém {reconciliation.WarehouseCode} " +
                $"({reconciliation.ReferenceDate:dd/MM/yyyy}) tem data igual ou posterior à {dateLabel}. " +
                $"Cancele a conferência antes de {action}.");
    }
}
