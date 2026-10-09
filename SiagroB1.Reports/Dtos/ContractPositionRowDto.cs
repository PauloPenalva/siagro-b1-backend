namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Linha de "Contratos — Compra x Venda": um contrato. <see cref="Group"/> = produto + UM;
/// <see cref="Section"/> = "Compras" ou "Vendas". <see cref="SignedBalanceQuantity"/> alimenta o
/// saldo geral do bloco: com os dois lados, compra soma e venda subtrai; com um lado só, é o
/// próprio saldo.
/// </summary>
public class ContractPositionRowDto
{
    public string Group { get; set; } = "";
    public string Section { get; set; } = "";
    public string Code { get; set; } = "";
    public string CreationDate { get; set; } = "";
    public string Partner { get; set; } = "";
    public string HarvestSeason { get; set; } = "";
    public string Type { get; set; } = "";
    public string CashFlowDate { get; set; } = "";
    public string DeliveryEndDate { get; set; } = "";
    public string Status { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public decimal Price { get; set; }
    public decimal ContractedQuantity { get; set; }
    public decimal DeliveredQuantity { get; set; }
    public decimal WashedOutQuantity { get; set; }
    public decimal BalanceQuantity { get; set; }
    public decimal SignedBalanceQuantity { get; set; }
}
