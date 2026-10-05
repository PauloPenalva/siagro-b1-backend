using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Infra.Nfe;

/// <summary>Consulta compartilhada pelo download do XML (Application) e pelo DANFE (Reports), na entrada.</summary>
public static class PurchaseInvoiceNfeXmlQueries
{
    /// <summary>procNFe autorizado mais recente do documento; sem ele, <see cref="NotFoundException"/>.</summary>
    public static async Task<string> LatestAuthorizedXmlAsync(this IQueryable<PurchaseInvoiceNfeXml> xmls, Guid invoiceKey) =>
        await xmls.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == invoiceKey && x.Kind == NfeXmlKind.Authorized)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .FirstOrDefaultAsync()
        ?? throw new NotFoundException("Este documento não tem NF-e autorizada.");
}
