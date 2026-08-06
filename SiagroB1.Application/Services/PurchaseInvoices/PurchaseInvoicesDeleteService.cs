using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Exclui o documento de entrada e suas linhas.
///
/// Só documento PENDENTE: depois de confirmado o caminho é estornar e cancelar, que preserva o
/// rastro. Mesma regra do documento de saída.
///
/// As linhas são removidas explicitamente porque <c>PurchaseInvoiceKey</c> é nulável — a relação é
/// opcional, e sem o RemoveRange elas ficariam órfãs com FK nula em vez de sumir. Tudo num único
/// SaveChanges, então é atômico sem precisar de transação explícita.
///
/// Não recebe userName: o registro deixa de existir, não há o que carimbar.
/// </summary>
public class PurchaseInvoicesDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key)
    {
        var invoice = await db.Context.PurchaseInvoices
                          .Include(x => x.Items)
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (invoice.InvoiceStatus != InvoiceStatus.Pending)
            throw new DefaultException(
                "Somente documento pendente pode ser excluído. Cancele o documento.");

        db.Context.PurchaseInvoicesItems.RemoveRange(invoice.Items);
        db.Context.PurchaseInvoices.Remove(invoice);

        await db.SaveChangesAsync();
    }
}
