using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Histórico de CC-e do documento de entrada (somente leitura).</summary>
public class PurchaseInvoicesNfeCorrectionsGetService(AppDbContext context)
{
    public IQueryable<PurchaseInvoiceNfeCorrection> QueryAll() => context.PurchaseInvoiceNfeCorrections.AsNoTracking();

    public IQueryable<PurchaseInvoiceNfeCorrection> QueryAll(Guid purchaseInvoiceKey) =>
        QueryAll().Where(x => x.PurchaseInvoiceKey == purchaseInvoiceKey).OrderByDescending(x => x.Sequence);
}
