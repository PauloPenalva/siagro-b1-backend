using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

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
    [Column(TypeName = "VARCHAR(20) NOT NULL")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    public bool Inactive { get; set; }

    /// <summary>Classe (CTT_CLASSE): só analítico recebe documento ou rateio.</summary>
    public AccountingEntityClass Class { get; set; } = AccountingEntityClass.Analytic;

    /// <summary>Centro superior (CTT_CCSUP); precisa ser sintético.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? ParentCode { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? ValidFrom { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime? ValidTo { get; set; }
}
