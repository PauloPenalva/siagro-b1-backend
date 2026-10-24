using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Ordem de frete (contrato de transporte) que origina CT-e e é faturada pela fatura de frete. Só STANDALONE.</summary>
[Table("FREIGHT_ORDERS")]
public class FreightOrder
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(14)")] public required string BranchCode { get; set; }
    public Guid DocNumberKey { get; set; }
    [Column(TypeName = "VARCHAR(20)")] public required string Code { get; set; }
    public DateTime OrderDate { get; set; }

    [Column(TypeName = "VARCHAR(15)")] public required string TakerCardCode { get; set; }
    [Column(TypeName = "VARCHAR(15)")] public string? SenderCardCode { get; set; }
    [Column(TypeName = "VARCHAR(15)")] public string? RecipientCardCode { get; set; }
    /// <summary>Nomes gravados na inclusão/alteração (snapshot): a lista e o detalhe exibem sem consultar o cadastro.</summary>
    [Column(TypeName = "VARCHAR(100)")] public string? TakerName { get; set; }
    [Column(TypeName = "VARCHAR(100)")] public string? SenderName { get; set; }
    [Column(TypeName = "VARCHAR(100)")] public string? RecipientName { get; set; }

    [Column(TypeName = "VARCHAR(7)")] public required string StartMunicipalityCode { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public required string StartState { get; set; }
    [Column(TypeName = "VARCHAR(7)")] public required string EndMunicipalityCode { get; set; }
    [Column(TypeName = "VARCHAR(2)")] public required string EndState { get; set; }
    [Column(TypeName = "VARCHAR(100)")] public string? StartMunicipalityName { get; set; }
    [Column(TypeName = "VARCHAR(100)")] public string? EndMunicipalityName { get; set; }

    [Column(TypeName = "VARCHAR(50)")] public required string ItemCode { get; set; }
    /// <summary>Vira o produto predominante do CT-e.</summary>
    [Column(TypeName = "VARCHAR(60)")] public required string ItemName { get; set; }

    [Column(TypeName = "DECIMAL(18,3)")] public decimal ContractedWeightKg { get; set; }
    public FreightCalculationMethod PriceMethod { get; set; }
    [Column(TypeName = "DECIMAL(18,6)")] public decimal UnitPrice { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public int? PaymentConditionCode { get; set; }
    public int? UsageCode { get; set; }
    [Column(TypeName = "VARCHAR(2000)")] public string? Observation { get; set; }

    public FreightOrderStatus Status { get; set; } = FreightOrderStatus.Open;
    public bool ClosedManually { get; set; }

    /// <summary>Peso já transportado (soma dos CT-e); persistido e mantido pelos serviços.</summary>
    [Column(TypeName = "DECIMAL(18,3)")] public decimal TransportedWeightKg { get; set; }

    [Column(TypeName = "VARCHAR(100)")] public required string CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
