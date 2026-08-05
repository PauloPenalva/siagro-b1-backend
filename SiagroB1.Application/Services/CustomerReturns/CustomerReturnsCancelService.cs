using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.CustomerReturns;

/// <summary>
/// Cancela a devolução do cliente.
///
/// Não há nada para estornar: o documento nunca moveu saldo, ledger ou romaneio. Cancelar
/// apenas tira o registro da conciliação e LIBERA a chave de NF-e para relançamento.
/// </summary>
public class CustomerReturnsCancelService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var customerReturn = await db.Context.CustomerReturns
                                 .FirstOrDefaultAsync(x => x.Key == key)
                             ?? throw new NotFoundException(
                                 "Devolução de cliente não encontrada.");

        if (customerReturn.Status == CustomerReturnStatus.Cancelled)
            throw new DefaultException("Devolução já está cancelada.");

        customerReturn.Status = CustomerReturnStatus.Cancelled;
        customerReturn.UpdatedAt = DateTime.Now;
        customerReturn.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
