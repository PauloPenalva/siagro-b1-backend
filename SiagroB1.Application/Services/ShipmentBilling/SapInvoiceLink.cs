namespace SiagroB1.Application.Services.ShipmentBilling;

/// <summary>
/// Dados da invoice de saída (A/R) do SAP Business One que o documento faturado passa a espelhar.
/// Só o fluxo de vínculo (GAC-1226) cria este registro; o controller público nunca o recebe.
/// </summary>
public record SapInvoiceLink(
    int DocEntry,
    int DocNum,
    string TaxDocumentNumber,
    string TaxDocumentSeries,
    string? AccessKey,
    string? Cfop);
