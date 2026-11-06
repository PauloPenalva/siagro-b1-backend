using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities.Market;

/// <summary>
/// Boleta de compra: congela a origem (tabela/linha), os insumos do cálculo e o passo a passo, mais os dados do
/// comprador. Tabela, linha, destino, instrumento e contrato são colunas simples, SEM FK, para que editar a tabela
/// de preços ou excluir o contrato nunca quebre a boleta.
/// </summary>
[Table("MARKET_TRADE_TICKETS")]
public class MarketTradeTicket
{
    [Key]
    public int Id { get; set; }

    public MarketTradeTicketStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }

    // Origem (sem FK)
    public int PriceTableId { get; set; }

    public int PriceTableLineId { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? PriceTableDescription { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? LineDescription { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? ItemName { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string HarvestSeasonCode { get; set; }

    [ForeignKey(nameof(HarvestSeasonCode))]
    public HarvestSeason? HarvestSeason { get; set; }

    [Column(TypeName = "VARCHAR(7) NOT NULL")]
    public required string MunicipalityCode { get; set; }

    [ForeignKey(nameof(MunicipalityCode))]
    public Municipality? Municipality { get; set; }

    public int DestinationId { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? DestinationName { get; set; }

    [Column(TypeName = "VARCHAR(30)")]
    public string? InstrumentCode { get; set; }

    // Insumos congelados
    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal ExchangeQuote { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal Premium { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal? NetFx { get; set; }

    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal? SpotFx { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal FreightPerTon { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal PortPrice { get; set; }

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal CalculatedPrice { get; set; }

    /// <summary>Passo a passo do cálculo (JSON).</summary>
    [Column(TypeName = "NVARCHAR(MAX)")]
    public string? StepsJson { get; set; }

    public DateTime? QuotedAt { get; set; }

    public MarketQuoteSource? QuoteSource { get; set; }

    /// <summary>Valor do ponto da curva de dólar usado no cálculo (nulo em instrumento cotado em R$).</summary>
    [Column(TypeName = "DECIMAL(18,6)")]
    public decimal? CurveFx { get; set; }

    /// <summary>Carimbo (UTC) do dólar MAIS ANTIGO entre o à vista e o da curva usados no cálculo.</summary>
    public DateTime? FxQuotedAt { get; set; }

    /// <summary>Origem da cotação do dólar mais antigo.</summary>
    public MarketQuoteSource? FxQuoteSource { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? DeliveryStartDate { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? DeliveryEndDate { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? ReceiptDate { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? PaymentDate { get; set; }

    // Comprador (código + nome copiado, sem FK)
    [Column(TypeName = "VARCHAR(10)")]
    public string? CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? CardName { get; set; }

    /// <summary>Volume em kg.</summary>
    [Column(TypeName = "DECIMAL(18,3)")]
    public decimal VolumeKg { get; set; }

    /// <summary>Preço negociado, R$ por saca de 60 kg.</summary>
    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal NegotiatedPrice { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? WarehouseCode { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? WarehouseName { get; set; }

    public int? AgentCode { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? AgentName { get; set; }

    [Column(TypeName = "VARCHAR(14)")]
    public string? BranchCode { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? LogisticRegionCode { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? Notes { get; set; }

    // Alçada
    [Column(TypeName = "DECIMAL(18,4)")]
    public decimal DeviationPercent { get; set; }

    public bool RequiresBoardApproval { get; set; }

    [Column(TypeName = "VARCHAR(200)")]
    public string? BoardApprovalReason { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? ApprovedBy { get; set; }

    public DateTime? ApprovedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? BoardApprovedBy { get; set; }

    public DateTime? BoardApprovedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? RejectedBy { get; set; }

    public DateTime? RejectedAt { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? RejectionReason { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CanceledBy { get; set; }

    public DateTime? CanceledAt { get; set; }

    // Contrato gerado (sem FK)
    public Guid? PurchaseContractKey { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? PurchaseContractCode { get; set; }

    /// <summary>Token de concorrência: dois aprovadores ao mesmo tempo geram um contrato só.</summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
