namespace SiagroB1.Domain.Interfaces;

/// <summary>Situação fiscal de uma A/R Invoice do SAP, vinda do add-on fiscal do cliente.</summary>
public record SapFiscalDocumentInfo(int DocEntry, string? AccessKey, bool Authorized, bool Cancelled, string? Protocol);

/// <summary>Invoice a consultar: DocEntry mais a filial (OINV.BPLId) que a identifica no add-on.</summary>
public record SapInvoiceRef(int DocEntry, int? BranchId);

/// <summary>
/// Fonte da chave de acesso e do status da NF-e das invoices do SAP. O SAP não guarda esses dados:
/// ficam no banco do add-on fiscal (InvOne, na Yokotobi).
/// </summary>
public interface ISapFiscalDocumentSource
{
    /// <summary>False quando não há add-on configurado.</summary>
    bool IsAvailable { get; }

    Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
        IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default);

    /// <summary>
    /// O mesmo para as NF de devolução de venda (A/R Credit Memo, ORIN). O DocEntry de ORIN é outra sequência:
    /// nunca consulte um Credit Memo por <see cref="GetForInvoicesAsync"/>. Padrão: nada (sem informação = não
    /// autorizada), para a fonte que não conhece Credit Memo.
    /// </summary>
    Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForCreditMemosAsync(
        IReadOnlyCollection<SapInvoiceRef> creditMemos, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(
            new Dictionary<int, SapFiscalDocumentInfo>());
}
