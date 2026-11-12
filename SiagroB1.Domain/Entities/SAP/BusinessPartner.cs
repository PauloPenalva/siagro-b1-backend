using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

[Table("OCRD")]
public class BusinessPartner : SiagroB1.Domain.Interfaces.ISapActivityFlags
{
    [Key]
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string CardName { get; set; }
    
    [Column("AliasName", TypeName = "VARCHAR(200) NOT NULL")]
    public string? CardFName { get; set; }
    
    /// <summary>
    /// BPType
    /// </summary>
    [Column(TypeName = "VARCHAR(1) NOT NULL")]
    public string? CardType { get; set; }
    
    [Column(name: "U_YKT_CNPJ_CPF",TypeName = "VARCHAR(14) NOT NULL")]
    public string? TaxId { get; set; }
    
    /// <summary>Nome do endereço de entrega padrão do parceiro (aponta para CRD1.Address).</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? ShipToDef { get; set; }

    [Column(TypeName = "VARCHAR(1)")]
    public string? QryGroup23 { get;  set; }
    
    public ICollection<Address> Addresses { get; set; } = new List<Address>();
    
    [Column("Free_Text", TypeName = "VARCHAR(100)")]
    public string? Notes { get; set; }

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