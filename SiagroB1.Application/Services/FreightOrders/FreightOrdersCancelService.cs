using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FreightOrders;

/// <summary>Cancela a ordem de frete; recusa se houver CT-e autorizado vinculado.</summary>
public class FreightOrdersCancelService(IUnitOfWork db, IConfiguration configuration)
{
    public async Task CancelAsync(Guid key, string user)
    {
        if (!ErpMode.IsStandalone(configuration))
            throw new DefaultException(BranchCteSettingsService.StandaloneOnly);
        var order = await db.Context.FreightOrders.FirstOrDefaultAsync(o => o.Key == key)
            ?? throw new DefaultException("Ordem de frete não encontrada.");
        if (order.Status == FreightOrderStatus.Cancelled) return;
        if (await db.Context.TransportDocuments.AnyAsync(d => d.FreightOrderKey == key && d.Status == CteStatus.Authorized))
            throw new DefaultException("A ordem de frete possui CT-e autorizado e não pode ser cancelada.");
        order.Status = FreightOrderStatus.Cancelled;
        await db.SaveChangesAsync();
    }
}
