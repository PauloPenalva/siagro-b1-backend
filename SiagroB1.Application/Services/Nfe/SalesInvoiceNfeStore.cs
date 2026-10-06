using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
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

    public Task<string?> LatestXmlAsync(Guid key, NfeXmlKind kind) =>
        db.Context.SalesInvoiceNfeXmls.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == key && x.Kind == kind)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Xml)
            .FirstOrDefaultAsync();

    public async Task<IReadOnlyList<int>> CorrectionSequencesAsync(Guid key) =>
        await db.Context.SalesInvoiceNfeCorrections.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == key)
            .OrderBy(x => x.Sequence)
            .Select(x => x.Sequence)
            .ToListAsync();

    public void AddCorrection(SalesInvoice document, int sequence, string text, NfeEventResult registered, string userName) =>
        db.Context.SalesInvoiceNfeCorrections.Add(new SalesInvoiceNfeCorrection
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = document.Key, Sequence = sequence, Text = text,
            Protocol = registered.Protocol, RegisteredAt = registered.RegisteredAt?.DateTime,
            // Reason é VARCHAR(255); NfeStatusText.Truncate corta em 500, então o corte é explícito.
            StatusCode = registered.StatusCode, Reason = registered.Reason[..Math.Min(255, registered.Reason.Length)],
            ProcEventXml = registered.ProcEventXml, CreatedAt = DateTime.Now, CreatedBy = userName,
        });

    public Task<string?> CorrectionXmlAsync(Guid key, int sequence) =>
        db.Context.SalesInvoiceNfeCorrections.AsNoTracking()
            .Where(x => x.SalesInvoiceKey == key && x.Sequence == sequence)
            .Select(x => x.ProcEventXml)
            .FirstOrDefaultAsync();
}
