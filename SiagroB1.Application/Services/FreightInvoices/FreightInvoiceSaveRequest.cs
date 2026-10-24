namespace SiagroB1.Application.Services.FreightInvoices;

/// <summary>
/// Corpo da ação OData de gravação da fatura de frete. O <see cref="FreightInvoiceSaveModel"/> é um record posicional
/// (sem construtor sem parâmetros), que o OData não consegue desserializar; este DTO o espelha com setters.
/// </summary>
public class FreightInvoiceSaveRequest
{
    public Guid? Key { get; set; }
    public required string BranchCode { get; set; }
    public required string TakerCardCode { get; set; }
    public DateTime IssueDate { get; set; }
    public DateTime? DueDate { get; set; }
    public int? PaymentConditionCode { get; set; }
    public string? Observation { get; set; }
    public List<Guid> TransportDocumentKeys { get; set; } = [];
    public Guid? DocNumberKey { get; set; }
    public string? TypedCode { get; set; }

    public FreightInvoiceSaveModel ToModel() => new(Key, BranchCode, TakerCardCode, IssueDate, DueDate,
        PaymentConditionCode, Observation, TransportDocumentKeys, DocNumberKey, TypedCode);
}
