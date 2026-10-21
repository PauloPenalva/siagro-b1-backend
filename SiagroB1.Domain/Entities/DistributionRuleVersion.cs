using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Versão de uma regra, vigente a partir de <see cref="ValidFrom"/> (apenas data).</summary>
[Table("DISTRIBUTION_RULE_VERSIONS")]
public class DistributionRuleVersion
{
    [Key]
    public Guid Id { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string RuleCode { get; set; } = string.Empty;

    [Column(TypeName = "DATE")]
    public DateTime ValidFrom { get; set; }

    public List<DistributionRuleLine> Lines { get; set; } = [];
}
