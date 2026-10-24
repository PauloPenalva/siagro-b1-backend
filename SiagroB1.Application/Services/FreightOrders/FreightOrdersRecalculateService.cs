using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightOrders;

/// <summary>
/// Recalcula o peso transportado e a situação da ordem de frete a partir dos CT-e autorizados. Chamado pelos hooks
/// do CT-e depois do commit dele, por isso grava com o próprio SaveChanges. Ordem cancelada ou encerrada
/// manualmente não muda de situação (só o peso é atualizado).
/// </summary>
public class FreightOrdersRecalculateService(IUnitOfWork db)
{
    public async Task RecalculateAsync(Guid orderKey)
    {
        var order = await db.Context.FreightOrders.FirstOrDefaultAsync(o => o.Key == orderKey);
        if (order is null) return;

        var ctes = await db.Context.TransportDocuments.AsNoTracking().Include(d => d.Quantities)
            .Where(d => d.FreightOrderKey == orderKey && d.Status == CteStatus.Authorized).ToListAsync();

        order.TransportedWeightKg = ctes.Sum(TransportDocumentWeight.GrossKg);

        if (order.Status != FreightOrderStatus.Cancelled && !order.ClosedManually)
        {
            order.Status = ctes.Count == 0 ? FreightOrderStatus.Open
                : order.ContractedWeightKg - order.TransportedWeightKg <= 0 ? FreightOrderStatus.Completed
                : FreightOrderStatus.InExecution;
        }

        await db.SaveChangesAsync();
    }
}
