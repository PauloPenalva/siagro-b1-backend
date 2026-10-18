using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.StorageAddresses;

/// <summary>
/// Reabre um lote encerrado. O job diário volta a calculá-lo a partir do dia seguinte; os dias em que
/// ficou encerrado só são recalculados pelo reprocessamento.
/// </summary>
public class StorageAddressesReopenService(IUnitOfWork db)
{
    public async Task ExecuteAsync(string code, string userName, CancellationToken ct = default)
    {
        var lot = await db.Context.StorageAddresses.FirstOrDefaultAsync(x => x.Code == code, ct)
                  ?? throw new NotFoundException($"Lote de armazenagem {code} não encontrado.");

        if (lot.Status != StorageAddressStatus.Closed)
            throw new ApplicationException($"O lote {code} não está encerrado.");

        lot.Status = StorageAddressStatus.Open;
        lot.ClosingDate = null;
        lot.UpdatedAt = DateTime.Now;
        lot.UpdatedBy = userName;

        await db.Context.SaveChangesAsync(ct);
    }
}
