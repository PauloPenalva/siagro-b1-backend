namespace SiagroB1.Reports.Dtos;

/// <summary>Filtros de "Contratos — Compra x Venda". Lado vazio = Ambos.</summary>
public class ContractPositionRequest : ContractPositionReportRequest
{
    public ContractPositionSide Side { get; set; } = ContractPositionSide.Both;
}
