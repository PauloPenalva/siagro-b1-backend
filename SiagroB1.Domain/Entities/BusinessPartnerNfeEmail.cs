using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>E-mail que recebe o DANFE e o XML das NF-e do parceiro. Até 10 por parceiro, únicos entre si.</summary>
[Table("BUSINESS_PARTNER_NFE_EMAILS")]
public class BusinessPartnerNfeEmail
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(250) NOT NULL")]
    public required string Email { get; set; }

    /// <summary>Posição na lista; o primeiro (0) também vai no <c>dest/email</c> do XML.</summary>
    public int LineNum { get; set; }

    public BusinessPartner? BusinessPartner { get; set; }
}
