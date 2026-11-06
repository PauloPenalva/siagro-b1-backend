using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>
/// Operação de bolsa registrada (o sistema não envia ordem). Contrato e EFP são colunas simples, SEM FK, para que
/// excluir o contrato ou o EFP nunca quebre a operação.
/// </summary>
[Table("MARKET_FUTURES_TRADES")]
[Index(nameof(AccountId), nameof(InstrumentCode), nameof(TradedAt))]
public class MarketFuturesTrade
{
    [Key]
    public int Id { get; set; }

    /// <summary>Instante do negócio (UTC).</summary>
    public DateTime TradedAt { get; set; }

    /// <summary>Data local do negócio.</summary>
    [Column(TypeName = "DATE")]
    public DateTime TradeDate { get; set; }

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

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal Fees { get; set; }

    public CurrencyType FeesCurrency { get; set; }

    public MarketFuturesTradeReason Reason { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? BrokerReference { get; set; }

    /// <summary>Lotes ainda abertos; mantido pelo casamento PEPS.</summary>
    public int OpenLots { get; set; }

    public bool Confirmed { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ConfirmedBy { get; set; }

    public DateTime? ConfirmedAt { get; set; }

    public Guid? PurchaseContractKey { get; set; }

    public Guid? SalesContractKey { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? ContractCode { get; set; }

    /// <summary>EFP que originou a operação (sem FK).</summary>
    public int? EfpId { get; set; }

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
