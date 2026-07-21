using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleaseMovementGuardService(AppDbContext context)
{
    /// <summary>
    /// Rejeita faturar contra uma liberação de venda não disponível: Completed (finalizada),
    /// Cancelled ou Paused (armazém não disponibiliza o produto). Chamado no faturamento
    /// (<c>ShipmentBillingCreateSalesInvoiceService</c>) antes de vincular os romaneios.
    /// </summary>
    public async Task EnsureCanBillAsync(Guid salesShipmentReleaseKey)
    {
        var status = await context.SalesShipmentReleases
            .Where(r => r.Key == salesShipmentReleaseKey)
            .Select(r => (ReleaseStatus?)r.Status)
            .FirstOrDefaultAsync();

        if (status is null)
            throw new ApplicationException("Liberação de entrega não encontrada.");

        if (status is ReleaseStatus.Completed or ReleaseStatus.Cancelled or ReleaseStatus.Paused)
            throw new ApplicationException(
                "Liberação de entrega finalizada/cancelada/pausada: não é possível faturar.");
    }
}
