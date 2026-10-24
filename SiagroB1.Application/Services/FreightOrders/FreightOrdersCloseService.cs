using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightOrders;

/// <summary>Encerra manualmente a ordem de frete (fica Concluída e o recálculo não a reabre).</summary>
public class FreightOrdersCloseService(IUnitOfWork db, IConfiguration configuration)
{
    public async Task CloseAsync(Guid key, string user)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchCteSettingsService.StandaloneOnly);
        var order = await db.Context.FreightOrders.FirstOrDefaultAsync(o => o.Key == key)
            ?? throw new DefaultException("Ordem de frete não encontrada.");
        if (order.Status == FreightOrderStatus.Cancelled)
            throw new DefaultException("Ordem de frete cancelada não pode ser encerrada.");
        order.Status = FreightOrderStatus.Completed;
        order.ClosedManually = true;
        await db.SaveChangesAsync();
    }
}
