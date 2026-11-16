namespace SiagroB1.Domain.Interfaces;

/// <summary>Situação fiscal de uma A/R Invoice do SAP, vinda do add-on fiscal do cliente.</summary>
public record SapFiscalDocumentInfo(int DocEntry, string? AccessKey, bool Authorized, bool Cancelled, string? Protocol);

/// <summary>
/// Fonte da chave de acesso e do status da NF-e das invoices do SAP. O SAP não guarda esses dados:
/// ficam no banco do add-on fiscal (InvOne, na Yokotobi).
/// </summary>
public interface ISapFiscalDocumentSource
{
    /// <summary>False quando não há add-on configurado.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
        IReadOnlyCollection<int> docEntries, CancellationToken ct = default);
}
