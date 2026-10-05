using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Documento de saída e seus XMLs para o pipeline da NF-e.</summary>
public sealed class SalesInvoiceNfeStore(IUnitOfWork db) : INfeDocumentStore<SalesInvoice>
{
    public string NotFoundMessage => "Documento de saída não encontrado.";

    public Task<SalesInvoice?> FindWithItemsAsync(Guid key) =>
        db.Context.SalesInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Key == key);

    public Task<SalesInvoice?> FindAsync(Guid key) =>
        db.Context.SalesInvoices.FirstOrDefaultAsync(i => i.Key == key);

    public Task<SalesInvoice?> FindReadOnlyAsync(Guid key) =>
        db.Context.SalesInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Key == key);

    public void AddXml(SalesInvoice document, NfeXmlKind kind, string xml) =>
        db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = document.Key, Kind = kind, Xml = xml, CreatedAt = DateTime.Now,
        });

    public async Task<IReadOnlyList<string>> SignedXmlsAsync(Guid key) =>
        await db.Context.SalesInvoiceNfeXmls.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == key && x.Kind == NfeXmlKind.Signed)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .ToListAsync();

    public Task<string> LatestAuthorizedXmlAsync(Guid key) => db.Context.SalesInvoiceNfeXmls.LatestAuthorizedXmlAsync(key);
}
