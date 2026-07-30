using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Dtos;

/// <summary>
/// Diagnóstico: contrato de venda cujo saldo por nota ficou NEGATIVO
/// (<c>TotalVolume − AllocatedVolume &lt; 0</c>), tipicamente por ter sido faturado pelo
/// fluxo legado, por fora da liberação de entrega. É a porta de entrada da tela de
/// conciliação — não dá para conciliar o que não se enxerga.
/// </summary>
public class SalesContractNegativeBalanceDto
{
    [JsonPropertyName("SalesContractKey")]
    public required string SalesContractKey { get; set; }

    [JsonPropertyName("RowId")]
    public int RowId { get; set; }

    [JsonPropertyName("Code")]
    public string? Code { get; set; }

    [JsonPropertyName("Complement")]
    public string? Complement { get; set; }

    [JsonPropertyName("BranchCode")]
    public string? BranchCode { get; set; }

    [JsonPropertyName("BranchName")]
    public string? BranchName { get; set; }

    [JsonPropertyName("CardCode")]
    public string? CardCode { get; set; }

    [JsonPropertyName("CardName")]
    public string? CardName { get; set; }

    [JsonPropertyName("ItemCode")]
    public string? ItemCode { get; set; }

    [JsonPropertyName("ItemName")]
    public string? ItemName { get; set; }

    [JsonPropertyName("UnitOfMeasureCode")]
    public string? UnitOfMeasureCode { get; set; }

    [JsonPropertyName("HarvestSeasonCode")]
    public string? HarvestSeasonCode { get; set; }

    [JsonPropertyName("TotalVolume")]
    public decimal TotalVolume { get; set; }

    [JsonPropertyName("AllocatedVolume")]
    public decimal AllocatedVolume { get; set; }

    /// <summary>Sempre negativo, por construção da consulta.</summary>
    [JsonPropertyName("Balance")]
    public decimal Balance { get; set; }
}
