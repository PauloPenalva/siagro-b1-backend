using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Centro de custo do SAP B1 (OPRC). Somente leitura — o cadastro é mantido no SAP.
/// </summary>
[Table("OPRC")]
public class CostCenter
{
    [Key]
    [Column("PrcCode")]
    public required string Code { get; set; }

    [Column("PrcName")]
    public required string Name { get; set; }

    /// <summary>OPRC.Active — 'Y'/'N'.</summary>
    [Column("Active", TypeName = "VARCHAR(1)")]
    public string? Active { get; set; }
}
