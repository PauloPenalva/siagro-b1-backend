using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Quem assina pela empresa. <see cref="BranchCode"/> nulo = assina por todas as filiais.
/// E-mail único: é a chave pela qual o webhook do provedor identifica quem assinou.
/// </summary>
[Table("COMPANY_SIGNATORIES")]
[Index(nameof(Email), IsUnique = true)]
public class CompanySignatory : BaseEntity
{
    [Column(TypeName = "VARCHAR(14)")]
    public string? BranchCode { get; set; }
    public virtual Branch? Branch { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    /// <summary>CPF, só dígitos.</summary>
    [Column(TypeName = "VARCHAR(14) NOT NULL")]
    public required string TaxId { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Email { get; set; }

    public SignatoryRole Role { get; set; } = SignatoryRole.SignAsParty;

    /// <summary>Ordem no bloco de assinaturas e no envio ao provedor.</summary>
    public int Order { get; set; }

    public bool Active { get; set; } = true;
}
