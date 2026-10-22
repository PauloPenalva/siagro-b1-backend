using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

[Table("TRUCKS")]
public class Truck 
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public string? Model { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string? City { get; set; }
    
    [Column(TypeName = "VARCHAR(2)")]
    [ForeignKey("State")]
    public string? StateKey { get; set; }
    public virtual State? State { get; set; }

    /// <summary>
    /// Tara do veículo em quilos. Nula de propósito: torná-la obrigatória travaria a gravação dos
    /// caminhões já cadastrados sem tara. Quem a cobra é a validação da pesagem, e só quando a
    /// balança tem <see cref="TruckScale.ValidateTare"/> ligado.
    /// </summary>
    public int? TareWeight { get; set; }

    [Column(TypeName = "VARCHAR(11)")]
    public string? Renavam { get; set; }

    /// <summary>Tração ou reboque. Os veículos antigos ficam como tração (default da coluna).</summary>
    public TruckKind Kind { get; set; } = TruckKind.Traction;

    /// <summary>Tipo de rodado (tpRod); só tração — reboque não tem.</summary>
    public TruckWheelType? WheelType { get; set; }

    /// <summary>Tipo de carroceria (tpCar).</summary>
    public TruckBodyType? BodyType { get; set; }

    /// <summary>Capacidade em quilos.</summary>
    public int? CapacityKg { get; set; }

    /// <summary>Capacidade em metros cúbicos.</summary>
    public int? CapacityM3 { get; set; }

    /// <summary>Próprio ou de terceiro. Só STANDALONE usa terceiro; em SAPB1 fica sempre próprio.</summary>
    public TruckOwnerType OwnerType { get; set; } = TruckOwnerType.Own;

    /// <summary>Parceiro proprietário (CardCode), sem navegação: em SAPB1 o parceiro vive no SAP.</summary>
    [Column(TypeName = "VARCHAR(15)")]
    public string? OwnerCardCode { get; set; }

    /// <summary>RNTRC do proprietário terceiro.</summary>
    [Column(TypeName = "VARCHAR(8)")]
    public string? OwnerRntrc { get; set; }

    /// <summary>Tipo do proprietário terceiro (tpProp).</summary>
    public TruckOwnerKind? OwnerKind { get; set; }

    /// <summary>Inativo some da escolha de veículo; os documentos antigos continuam abrindo.</summary>
    public bool Inactive { get; set; }
}
