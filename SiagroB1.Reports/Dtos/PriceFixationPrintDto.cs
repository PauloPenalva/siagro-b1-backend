namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Espelho de fixação de preço: comprovante enviado ao produtor/fornecedor
/// confirmando o preço fixado para uma parcela do contrato a fixar (PAF).
/// </summary>
public class PriceFixationPrintDto
{
    public string? Title { get; set; } = "ESPELHO DE FIXAÇÃO DE PREÇO";

    public string? CompanyName { get; set; }
    public string? CompanyTaxId { get; set; }

    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public string? TaxId { get; set; }
    public string? Street { get; set; }
    public string? CityStateZip { get; set; }

    public string? ContractCode { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public string? HarvestSeasonName { get; set; }
    public string? UnitOfMeasureCode { get; set; }

    public DateTime? FixationDate { get; set; }
    public decimal FixationVolume { get; set; }
    public decimal FixationPrice { get; set; }
    public decimal FreightCost { get; set; }

    /// <summary>Volume × preço: o valor efetivamente comprometido nesta fixação.</summary>
    public decimal FixationTotal { get; set; }

    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovalComments { get; set; }

    /// <summary>Posição do contrato no momento da emissão, para o produtor conferir.</summary>
    public decimal ContractTotalVolume { get; set; }
    public decimal ContractFixedVolume { get; set; }
    public decimal ContractAvailableVolumeToPricing { get; set; }
}
