using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Infra.Nfe;

/// <summary>Consulta compartilhada pelo download do XML (Application) e pelo DANFE (Reports).</summary>
public static class SalesInvoiceNfeXmlQueries
{
    /// <summary>procNFe autorizado mais recente do documento; sem ele, <see cref="NotFoundException"/>.</summary>
    public static async Task<string> LatestAuthorizedXmlAsync(this IQueryable<SalesInvoiceNfeXml> xmls, Guid invoiceKey) =>
        await xmls.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == invoiceKey && x.Kind == SalesInvoiceNfeXmlKind.Authorized)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .FirstOrDefaultAsync()
        ?? throw new NotFoundException("Este documento não tem NF-e autorizada.");
}
