using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Nfe;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>Documento de entrada e seus XMLs para o pipeline da NF-e.</summary>
public sealed class PurchaseInvoiceNfeStore(IUnitOfWork db) : INfeDocumentStore<PurchaseInvoice>
{
    public string NotFoundMessage => "Documento de entrada não encontrado.";

    public Task<PurchaseInvoice?> FindWithItemsAsync(Guid key) =>
        db.Context.PurchaseInvoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Key == key);

    public Task<PurchaseInvoice?> FindAsync(Guid key) =>
        db.Context.PurchaseInvoices.FirstOrDefaultAsync(i => i.Key == key);

    public Task<PurchaseInvoice?> FindReadOnlyAsync(Guid key) =>
        db.Context.PurchaseInvoices.AsNoTracking().FirstOrDefaultAsync(i => i.Key == key);

    public void AddXml(PurchaseInvoice document, NfeXmlKind kind, string xml) =>
        db.Context.PurchaseInvoiceNfeXmls.Add(new PurchaseInvoiceNfeXml
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = document.Key, Kind = kind, Xml = xml, CreatedAt = DateTime.Now,
        });

    public async Task<IReadOnlyList<string>> SignedXmlsAsync(Guid key) =>
        await db.Context.PurchaseInvoiceNfeXmls.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == key && x.Kind == NfeXmlKind.Signed)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .ToListAsync();

    public Task<string> LatestAuthorizedXmlAsync(Guid key) => db.Context.PurchaseInvoiceNfeXmls.LatestAuthorizedXmlAsync(key);
}
