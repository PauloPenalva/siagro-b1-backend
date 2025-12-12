using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("PROCESSING_COSTS")]
public class ProcessingCost 
{
    [Key] 
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }
    
    [Required(ErrorMessage = "Descrição é obrigatório.")]
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }
    
    /// <summary>
    /// custo de armazenagem
    /// </summary>
    [Column(TypeName = "DECIMAL(18,8) NOT NULL")]
    public decimal? StoragePrice { get; set; }
    
    /// <summary>
    /// carencia de armazenagem
    /// </summary>
    public int? StorageGraceDays { get; set; }
    
    /// <summary>
    /// intervalo de cobrança de armazenagem
    /// </summary>
    public int? StorageBillingIntervalDays { get; set; }

    /// <summary>
    /// pt-br: Taxa de expurgo
    /// </summary>
    [Column(TypeName = "DECIMAL(18,8) NOT NULL")]
    public decimal? FumigationPrice { get; set; }

    /// <summary>
    /// pt-br: Vencimento em dias para cobrança do serviço de expurgo
    /// </summary>
    public int? FumigationIntervalDays { get; set; } = 0;

    /// <summary>
    /// carencia quebra técnica em dias
    /// </summary>
    public int? TechnicalLossGraceDays { get; set; } = 0;

    /// <summary>
    /// vencimento quebra técnica em dias
    /// </summary>
    public int? TechnicalLossIntervalDays { get; set; } = 0;

    /// <summary>
    /// percentual de quebra técnica
    /// </summary>
    [Column(TypeName = "DECIMAL(18,8) NOT NULL")]
    public decimal? TechnicalLossRate { get; set; }

    public ICollection<ProcessingCostDryingParameter> DryingParameters { get; set; } = [];

    public ICollection<ProcessingCostDryingDetail> DryingDetails { get; set; } = [];

    public ICollection<ProcessingCostQualityParameter> QualityParameters { get; set; } = [];

    public ICollection<ProcessingCostServiceDetail> ServiceDetails { get; set; } = [];
}
