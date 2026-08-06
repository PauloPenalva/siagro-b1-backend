using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

public class PurchaseInvoicesChangeLogsGetService(AppDbContext context)
{
    /// <summary>
    /// Log de alterações de um documento de entrada, do mais recente para o mais antigo — é a ordem
    /// em que a tela pergunta "o que mudou por último?".
    /// </summary>
    public IQueryable<PurchaseInvoiceChangeLog> QueryAll(Guid purchaseInvoiceKey) =>
        context.PurchaseInvoicesChangeLogs
            .AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == purchaseInvoiceKey)
            .OrderByDescending(x => x.ChangedAt);
}
