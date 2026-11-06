using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>Hedge de moeda (NDF ou termo) registrado; o resultado é informativo, sem lançamento financeiro.</summary>
[Table("MARKET_FX_HEDGES")]
public class MarketFxHedge
{
    [Key]
    public int Id { get; set; }

    /// <summary>Instante do negócio (UTC).</summary>
    public DateTime TradedAt { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime TradeDate { get; set; }

    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public MarketCounterpartyAccount? Account { get; set; }

    public MarketFxHedgeKind Kind { get; set; }

    public MarketTradeSide Side { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal AmountUsd { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal ContractedRate { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime SettlementDate { get; set; }

    public MarketFxHedgeStatus Status { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal? SettlementRate { get; set; }

    public DateTime? SettledAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? SettledBy { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal? RealizedPnlBrl { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? BankReference { get; set; }

    public bool Confirmed { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ConfirmedBy { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public Guid? PurchaseContractKey { get; set; }

    public Guid? SalesContractKey { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? ContractCode { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
