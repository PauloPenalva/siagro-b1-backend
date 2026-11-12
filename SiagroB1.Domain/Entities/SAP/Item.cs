using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Domain.Entities.SAP;

[Table("OITM")]
public class Item : ISapActivityFlags
{
    [Key]
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string ItemName { get; set; }
    
    public short? ItmsGrpCod {get; set;}
    
    [Column("U_YKT_Integra_Siagro", TypeName = "VARCHAR(3)")]
    public string? Enabled { get; set; }

    /// <summary>OCRD/OITM.validFor — 'Y' quando o cadastro está marcado como Ativo (opcionalmente só entre validFrom e validTo).</summary>
    [Column("validFor", TypeName = "VARCHAR(1)")]
    public string? ValidFor { get; set; }

    [Column("validFrom")]
    public DateTime? ValidFrom { get; set; }

    [Column("validTo")]
    public DateTime? ValidTo { get; set; }

    /// <summary>OCRD/OITM.frozenFor — 'Y' quando o cadastro está marcado como Inativo (opcionalmente só entre frozenFrom e frozenTo).</summary>
    [Column("frozenFor", TypeName = "VARCHAR(1)")]
    public string? FrozenFor { get; set; }

    [Column("frozenFrom")]
    public DateTime? FrozenFrom { get; set; }

    [Column("frozenTo")]
    public DateTime? FrozenTo { get; set; }
}