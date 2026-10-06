using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Histórico de CC-e do documento de saída (somente leitura).</summary>
public class SalesInvoicesNfeCorrectionsGetService(AppDbContext context)
{
    public IQueryable<SalesInvoiceNfeCorrection> QueryAll() => context.SalesInvoiceNfeCorrections.AsNoTracking();

    public IQueryable<SalesInvoiceNfeCorrection> QueryAll(Guid salesInvoiceKey) =>
        QueryAll().Where(x => x.SalesInvoiceKey == salesInvoiceKey).OrderByDescending(x => x.Sequence);
}
