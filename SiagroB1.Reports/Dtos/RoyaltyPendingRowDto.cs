// SiagroB1.Reports/Dtos/RoyaltyPendingRowDto.cs
namespace SiagroB1.Reports.Dtos;

public enum RoyaltyPendingRowKind
{
    Item = 0,
    /// <summary>Subtotal do parceiro dentro da situação.</summary>
    Partner = 1,
    /// <summary>Subtotal da situação.</summary>
    Situation = 2,
}

public class RoyaltyPendingRowDto
{
    public RoyaltyPendingRowKind Kind { get; set; }
    /// <summary>Linhas de subtotal saem em negrito no template.</summary>
    public bool IsSubtotal => Kind != RoyaltyPendingRowKind.Item;
    public string Situation { get; set; } = "";
    /// <summary>"Recebimento" ou "Compra".</summary>
    public string Type { get; set; } = "";
    public string Code { get; set; } = "";
    public string Date { get; set; } = "";
    public string Partner { get; set; } = "";
    public string Origin { get; set; } = "";
    public string TestCode { get; set; } = "";
    public decimal NetWeight { get; set; }
    public decimal WrittenOff { get; set; }
    public decimal Balance { get; set; }
}

public record RoyaltyPendingData(List<RoyaltyPendingRowDto> Rows, int ItemCount,
    decimal TotalNetWeight, decimal TotalWrittenOff, decimal TotalBalance);
