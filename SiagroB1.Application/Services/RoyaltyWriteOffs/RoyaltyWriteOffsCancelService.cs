using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.RoyaltyWriteOffs;

/// <summary>Cancela a baixa (o ITS foi estornado/corrigido lá): o saldo volta aos romaneios.</summary>
public class RoyaltyWriteOffsCancelService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var writeOff = await db.Context.RoyaltyWriteOffs.FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Baixa ITS não encontrada.");

        if (writeOff.Status == RoyaltyWriteOffStatus.Cancelled)
            throw new ApplicationException("Esta baixa ITS já está cancelada.");

        writeOff.Status = RoyaltyWriteOffStatus.Cancelled;
        writeOff.CanceledBy = userName;
        writeOff.CanceledAt = DateTime.Now;
        writeOff.UpdatedBy = userName;
        writeOff.UpdatedAt = DateTime.Now;

        await db.SaveChangesAsync();
    }
}
