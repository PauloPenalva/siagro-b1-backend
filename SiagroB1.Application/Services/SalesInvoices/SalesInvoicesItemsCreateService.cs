using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesInvoices;

public class SalesInvoicesItemsCreateService(
    IUnitOfWork db,
    IItemService itemService,
    SalesInvoicesTaxApplyService taxApply,
    ILogger<SalesInvoicesItemsCreateService> logger)
{
    public async Task ExecuteAsync(SalesInvoiceItem salesInvoiceItem, string userName)
    {
        // Tributação da NF-e STANDALONE: calcula a linha nova antes de gravar. Fora do try para a
        // guarda chegar à tela como 400, e não embrulhada em ApplicationException. No-op com a
        // regra inativa.
        var invoice = await db.Context.SalesInvoices
            .FirstOrDefaultAsync(x => x.Key == salesInvoiceItem.SalesInvoiceKey);

        if (invoice is not null)
        {
            SalesInvoiceNfeLock.EnsureLinesChangeable(invoice.NfeStatus);
            await taxApply.ApplyAsync(invoice, [salesInvoiceItem]);
        }

        try
        {
            salesInvoiceItem.ItemName = (await itemService.GetByIdAsync(salesInvoiceItem.ItemCode))?.ItemName;

            await db.Context.SalesInvoicesItems.AddAsync(salesInvoiceItem);
            await db.SaveChangesAsync();

            // Numa devolução o peso do cabeçalho é a soma das linhas. Depois do flush: a soma
            // agrega no SERVIDOR e leria o estado sem esta linha.
            await SalesInvoicesReturnWeightService.RecalculateAsync(
                db.Context, salesInvoiceItem.SalesInvoiceKey);

            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError("Error: {message}", e.Message);
            throw new ApplicationException(e.Message);
        }
    }
}