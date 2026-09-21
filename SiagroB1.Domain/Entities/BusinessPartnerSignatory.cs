using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Quem assina pelo parceiro. <see cref="CardCode"/> SEM FK e sem navegação: em modo SAPB1 a
/// tabela local BUSINESS_PARTNERS está vazia (mesma razão de <see cref="SalesContract.CardFName"/>).
/// A mesma pessoa pode assinar por dois parceiros, mas não duas vezes pelo mesmo.
/// </summary>
[Table("BUSINESS_PARTNER_SIGNATORIES")]
[Index(nameof(CardCode))]
[Index(nameof(CardCode), nameof(Email), IsUnique = true)]
public class BusinessPartnerSignatory : BaseEntity
{
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string CardCode { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(14) NOT NULL")]
    public required string TaxId { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Email { get; set; }

    public SignatoryRole Role { get; set; } = SignatoryRole.SignAsParty;

    public int Order { get; set; }

    public bool Active { get; set; } = true;
}
