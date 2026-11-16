using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.SAP;

/// <summary>Sem add-on fiscal configurado (ou modo STANDALONE): nunca devolve nada.</summary>
public class NullSapFiscalDocumentSource : ISapFiscalDocumentSource
{
    public bool IsAvailable => false;

    public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
        IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(
            new Dictionary<int, SapFiscalDocumentInfo>());
}
