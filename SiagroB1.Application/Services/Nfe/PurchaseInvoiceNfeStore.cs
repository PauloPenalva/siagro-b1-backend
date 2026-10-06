using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
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

    public Task<string?> LatestXmlAsync(Guid key, NfeXmlKind kind) =>
        db.Context.PurchaseInvoiceNfeXmls.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == key && x.Kind == kind)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<int>> CorrectionSequencesAsync(Guid key) =>
        await db.Context.PurchaseInvoiceNfeCorrections.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == key)
            .OrderBy(x => x.Sequence)
            .Select(x => x.Sequence)
            .ToListAsync();

    public void AddCorrection(PurchaseInvoice document, int sequence, string text, NfeEventResult registered, string userName) =>
        db.Context.PurchaseInvoiceNfeCorrections.Add(new PurchaseInvoiceNfeCorrection
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = document.Key, Sequence = sequence, Text = text,
            Protocol = registered.Protocol, RegisteredAt = registered.RegisteredAt?.DateTime,
            // Reason é VARCHAR(255); NfeStatusText.Truncate corta em 500, então o corte é explícito.
            StatusCode = registered.StatusCode, Reason = registered.Reason[..Math.Min(255, registered.Reason.Length)],
            ProcEventXml = registered.ProcEventXml, CreatedAt = DateTime.Now, CreatedBy = userName,
        });

    public Task<string?> CorrectionXmlAsync(Guid key, int sequence) =>
        db.Context.PurchaseInvoiceNfeCorrections.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == key && x.Sequence == sequence)
            .Select(x => x.ProcEventXml)
            .FirstOrDefaultAsync();
}
