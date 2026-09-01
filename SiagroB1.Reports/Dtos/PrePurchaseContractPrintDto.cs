namespace SiagroB1.Reports.Dtos;

public class PrePurchaseContractPrintDto
{
    public string? Title { get; set; } = "PRÉ - CONTRATO DE COMPRA DE CEREAIS";

    // Compradora (filial)
    public string? CompanyName { get; set; }
    public string? CompanyTaxId { get; set; }

    // Vendedor (parceiro de negócios)
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public string? Cnpj { get; set; }
    public string? Cpf { get; set; }
    public string? StateRegistration { get; set; }
    public string? ManagingPartners { get; set; }
    public string? ContractContact { get; set; }
    public string? Street { get; set; }

    // Contrato
    public string? Code { get; set; }
    public DateTime? CreationDate { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public string? HarvestSeasonName { get; set; }
    public decimal TotalVolume { get; set; }
    public string? UnitOfMeasureCode { get; set; }
    public decimal StandardPrice { get; set; }
    public string? PaymentTerms { get; set; }
    public DateTime? StandardCashFlowDate { get; set; }

    // Entrega / retirada
    public DateTime DeliveryStartDate { get; set; }
    public DateTime DeliveryEndDate { get; set; }
    public string? DeliveryLocationName { get; set; }
    public string? WarehouseTaxId { get; set; }

    // Frete e funrural
    public string? FreightTermsText { get; set; }
    public decimal FreightCostStandard { get; set; }
    public string? FreightUmCode { get; set; }
    public string? FunruralTypeText { get; set; }

    public string? Comments { get; set; }

    public List<PrePurchaseContractQualityDto> QualityParameters { get; set; } = [];

    public List<PrePurchaseContractTaxDto> Taxes { get; set; } = [];
}

public class PrePurchaseContractQualityDto
{
    public string? AttribCode { get; set; }
    public string? AttribName { get; set; }
    public decimal MaxLimitRate { get; set; }
}

public class PrePurchaseContractTaxDto
{
    public string? TaxCode { get; set; }
    public string? TaxName { get; set; }
    public decimal Rate { get; set; }
    public decimal TotalTax { get; set; }
}
