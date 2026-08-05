using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.CustomerReturns;

/// <summary>
/// Atualiza a devolução — na prática, a AMARRAÇÃO das linhas com as notas de origem, que é
/// manual e costuma ser feita depois de importar o XML.
/// </summary>
public class CustomerReturnsUpdateService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, CustomerReturn entity, string userName)
    {
        var existing = await db.Context.CustomerReturns
                           .Include(x => x.Items)
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException(
                           "Devolução de cliente não encontrada.");

        if (existing.Status == CustomerReturnStatus.Cancelled)
            throw new DefaultException("Devolução cancelada não pode ser alterada.");

        existing.DocumentNumber = entity.DocumentNumber;
        existing.DocumentSeries = entity.DocumentSeries;
        existing.IssueDate = entity.IssueDate;
        existing.TotalValue = entity.TotalValue;
        existing.TaxPayerComments = entity.TaxPayerComments;
        existing.UpdatedAt = DateTime.Now;
        existing.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
