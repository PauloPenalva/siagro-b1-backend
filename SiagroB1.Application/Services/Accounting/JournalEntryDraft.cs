using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>Lançamento a gravar: o que o manual, o estorno e a contabilização automática entregam ao serviço de gravação.</summary>
public class JournalEntryDraft
{
    public DateTime PostingDate { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string Batch { get; set; } = string.Empty;
    public string SubBatch { get; set; } = "001";
    public JournalEntryOrigin Origin { get; set; }
    public string? StandardPostingCode { get; set; }
    public string? SourceDocumentType { get; set; }
    public string? SourceDocumentKey { get; set; }
    public string? SourceEvent { get; set; }
    public int? ReversalOfDocEntry { get; set; }
    public string? Comments { get; set; }
    public List<JournalEntryLineDraft> Lines { get; set; } = new();
}

/// <summary>Linha a gravar. O histórico pode passar de 40 caracteres: o excedente vira linhas tipo 4.</summary>
public class JournalEntryLineDraft
{
    public JournalEntryLineType EntryType { get; set; }
    public string? DebitAccountCode { get; set; }
    public string? CreditAccountCode { get; set; }
    public decimal Amount { get; set; }
    public string History { get; set; } = string.Empty;
    public string? DebitCostCenterCode { get; set; }
    public string? CreditCostCenterCode { get; set; }
    public string? DebitAccountingItemCode { get; set; }
    public string? CreditAccountingItemCode { get; set; }
    public string? StandardPostingSequence { get; set; }
    public string? BusinessPartnerCode { get; set; }
}
