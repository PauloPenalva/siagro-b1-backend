using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Centro de custo mantido localmente (modo STANDALONE).
/// Em modo SAPB1 esta tabela fica vazia e o dado vem de OPRC — ver
/// <see cref="SAP.CostCenter"/>.
/// </summary>
[Table("COST_CENTERS")]
public class CostCenter
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    public bool Inactive { get; set; }
}
