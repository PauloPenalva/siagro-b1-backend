namespace SiagroB1.Application.Services.FreightInvoices;

/// <summary>Campos editáveis do rascunho da fatura de frete. <see cref="Key"/> nulo = inclusão.</summary>
public record FreightInvoiceSaveModel(
    Guid? Key,
    string BranchCode,
    string TakerCardCode,
    DateTime IssueDate,
    DateTime? DueDate,
    int? PaymentConditionCode,
    string? Observation,
    IReadOnlyList<Guid> TransportDocumentKeys,
    Guid? DocNumberKey,
    string? TypedCode);
