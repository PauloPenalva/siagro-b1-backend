namespace SiagroB1.Domain.Dtos;

/// <summary>Uma liberação do armazém+produto com o saldo a embarcar na data e hoje (GAC-1164 §9).</summary>
public class WarehouseReconciliationReleaseBalanceDto
{
    public Guid ShipmentReleaseKey { get; set; }
    public DateTime ReleaseDate { get; set; }
    /// <summary>Nome do enum <c>ReleaseStatus</c> (string, para a tela não depender da serialização de enum).</summary>
    public string Status { get; set; } = "";
    /// <summary>Nome do enum <c>ReleaseOrigin</c>.</summary>
    public string Origin { get; set; } = "";
    public Guid PurchaseContractKey { get; set; }
    public string? PurchaseContractCode { get; set; }
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public decimal BalanceAtReferenceDate { get; set; }
    public decimal CurrentBalance { get; set; }
    /// <summary>Liberada e com saldo hoje. Pausada soma no saldo mas não recebe perda.</summary>
    public bool CanReceiveLoss { get; set; }
}
