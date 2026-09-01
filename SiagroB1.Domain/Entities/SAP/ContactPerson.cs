using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.SAP;

/// <summary>
/// Pessoas de contato do parceiro no SAP Business One (OCPR). O pré-contrato usa
/// <see cref="Position"/> como classificador: "Socio" traz os sócios administradores
/// (com o CPF em <see cref="Notes1"/>) e "Contrato" traz o contato para envio do
/// contrato. Como o texto é digitado à mão, a comparação é feita normalizada.
/// </summary>
[Table("OCPR")]
public class ContactPerson
{
    [Key]
    public int CntctCode { get; set; }

    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    public string? Name { get; set; }

    /// <summary>Cargo, digitado livremente. Classifica o contato no pré-contrato.</summary>
    public string? Position { get; set; }

    /// <summary>Observações. Nos sócios administradores, guarda o CPF.</summary>
    public string? Notes1 { get; set; }

    [Column("E_MailL")]
    public string? Email { get; set; }

    [Column("Cellolar")]
    public string? MobilePhone { get; set; }

    [Column("Tel1")]
    public string? Phone { get; set; }
}
