using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Snapshot de um signatário no momento do envio (Fase 2). POCO como os anexos: não é entidade
/// de negócio com auditoria própria. Criada já na Fase 1 para a migration nascer completa.
/// </summary>
[Table("CONTRACT_DRAFT_SIGNERS")]
[Index(nameof(DraftKey))]
public class ContractDraftSigner
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Key { get; set; }

    public Guid DraftKey { get; set; }
    public virtual ContractDraft? Draft { get; set; }

    public SignerSide Side { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(14) NOT NULL")]
    public required string TaxId { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string Email { get; set; }

    public SignatoryRole Role { get; set; }

    public int Order { get; set; }

    public SignerStatus Status { get; set; } = SignerStatus.Pending;

    public DateTime? SignedAt { get; set; }

    [Column(TypeName = "VARCHAR(500)")]
    public string? LastMessage { get; set; }
}
