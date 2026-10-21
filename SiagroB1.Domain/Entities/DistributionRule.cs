using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Regra de distribuição: rateio pré-configurado (CTJ): reparte um valor entre CC/Item Contábil, por vigência.</summary>
[Table("DISTRIBUTION_RULES")]
public class DistributionRule
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    public bool Inactive { get; set; }

    public List<DistributionRuleVersion> Versions { get; set; } = [];
}
