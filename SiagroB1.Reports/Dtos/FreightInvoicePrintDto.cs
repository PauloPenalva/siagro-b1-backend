namespace SiagroB1.Reports.Dtos;

/// <summary>Cabeçalho do impresso da fatura de frete. Textos já formatados em pt-BR.</summary>
public class FreightInvoicePrintDto
{
    public string InvoiceNumber { get; set; } = "";
    public string IssueDate { get; set; } = "";
    public string Status { get; set; } = "";

    public string EmitterName { get; set; } = "";
    public string EmitterTaxId { get; set; } = "";
    public string EmitterAddress { get; set; } = "";
    public string EmitterPhone { get; set; } = "";

    public string TakerCode { get; set; } = "";
    public string TakerName { get; set; } = "";
    public string TakerTaxId { get; set; } = "";
    public string TakerAddress { get; set; } = "";

    public string PaymentCondition { get; set; } = "";
    /// <summary>Dados bancários da conta de recebimento; vazio quando não há conta óbvia (o bloco some).</summary>
    public string BankInfo { get; set; } = "";

    public decimal TotalAmount { get; set; }
    public decimal TotalWeightKg { get; set; }
    public string? Observation { get; set; }

    public List<FreightInvoicePrintInstallmentDto> Installments { get; set; } = [];
    public List<FreightInvoicePrintItemDto> Items { get; set; } = [];
}

public class FreightInvoicePrintInstallmentDto
{
    public string Installment { get; set; } = "";
    public string DueDate { get; set; } = "";
    public decimal Amount { get; set; }
}

public class FreightInvoicePrintItemDto
{
    public int LineNum { get; set; }
    public string Number { get; set; } = "";
    public string Series { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string IssuedAt { get; set; } = "";
    public string Route { get; set; } = "";
    public decimal WeightKg { get; set; }
    public decimal Amount { get; set; }
}
