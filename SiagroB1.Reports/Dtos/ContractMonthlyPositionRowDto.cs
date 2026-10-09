namespace SiagroB1.Reports.Dtos;

/// <summary>
/// Linha de "Posição Comprado x Vendido por Mês": um prazo (Vencido, mm/aaaa ou Sem prazo) de um
/// grupo produto + safra + UM, com o saldo a entregar de compra e venda, o líquido e o líquido
/// acumulado desde o Vencido. Os campos Contracted*/Delivered*/WashedOut* são do GRUPO (repetidos
/// em todas as linhas dele) e saem no cabeçalho do grupo.
/// </summary>
public class ContractMonthlyPositionRowDto
{
    public string Group { get; set; } = "";
    public string UnitOfMeasure { get; set; } = "";
    public string Bucket { get; set; } = "";
    public decimal ContractedPurchase { get; set; }
    public decimal ContractedSales { get; set; }
    public decimal ContractedNet { get; set; }
    public decimal DeliveredPurchase { get; set; }
    public decimal DeliveredSales { get; set; }
    public decimal DeliveredNet { get; set; }
    public decimal WashedOutPurchase { get; set; }
    public decimal PurchaseQuantity { get; set; }
    public decimal SalesQuantity { get; set; }
    public decimal NetQuantity { get; set; }
    public decimal AccumulatedQuantity { get; set; }
}
