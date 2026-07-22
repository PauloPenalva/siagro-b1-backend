using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Totais de reconciliação das alocações de um contrato de venda (header do detalhe):
/// valor entregue ao preço do contrato, valor faturado ao preço da NF e a diferença.
/// Tudo pelo volume alocado (reflete realocações).
/// </summary>
public class SalesContractAllocationTotalsDto
{
    /// <summary>Σ(Volume alocado × preço do contrato).</summary>
    [JsonPropertyName("TotalDelivered")]
    public decimal TotalDelivered { get; set; }

    /// <summary>Σ(Volume alocado × preço unitário da NF).</summary>
    [JsonPropertyName("TotalInvoices")]
    public decimal TotalInvoices { get; set; }

    /// <summary>TotalInvoices − TotalDelivered (positivo = NF cobrou mais que o contrato).</summary>
    [JsonPropertyName("TotalDifference")]
    public decimal TotalDifference { get; set; }
}
