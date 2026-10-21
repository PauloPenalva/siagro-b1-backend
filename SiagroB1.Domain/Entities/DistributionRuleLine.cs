using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Linha de uma versão: centro de custo e percentual. A soma das linhas da versão é 100.</summary>
[Table("DISTRIBUTION_RULE_LINES")]
public class DistributionRuleLine
{
    [Key]
    public Guid Id { get; set; }

    public Guid VersionId { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string CostCenterCode { get; set; } = string.Empty;

    [Column(TypeName = "DECIMAL(9,4)")]
    public decimal Percentage { get; set; }
}
