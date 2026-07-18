namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Dados do parceiro de negócios necessários aos relatórios, independentes de a
/// origem ser o banco do Siagro (standalone) ou o do SAP Business One.
/// </summary>
public class ReportPartnerDto
{
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public string? TaxId { get; set; }
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
}
