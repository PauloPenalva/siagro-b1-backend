using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>
/// EFP (troca de futuro por físico): fixa o preço de um contrato a fixar e gera a operação de bolsa. Contrato,
/// operação e fixação são colunas simples, SEM FK.
/// </summary>
[Table("MARKET_EFPS")]
public class MarketEfp
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime Date { get; set; }

    public int AccountId { get; set; }

    [ForeignKey(nameof(AccountId))]
    public MarketCounterpartyAccount? Account { get; set; }

    [Column(TypeName = "VARCHAR(30) NOT NULL")]
    public required string InstrumentCode { get; set; }

    [ForeignKey(nameof(InstrumentCode))]
    public MarketInstrument? Instrument { get; set; }

    public MarketTradeSide Side { get; set; }

    public int Lots { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal Price { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal Premium { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal? FxRate { get; set; }

    public MarketEfpContractSide ContractSide { get; set; }

    public Guid ContractKey { get; set; }

    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ContractCode { get; set; }

    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal VolumeKg { get; set; }

    /// <summary>Preço da fixação por kg.</summary>
    [Column(TypeName = "DECIMAL(18,8)")]
    public decimal FixationPrice { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime FinancialDueDate { get; set; }

    public int? FuturesTradeId { get; set; }

    public Guid? FixationKey { get; set; }

    public MarketEfpStatus Status { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CanceledBy { get; set; }

    public DateTime? CanceledAt { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
