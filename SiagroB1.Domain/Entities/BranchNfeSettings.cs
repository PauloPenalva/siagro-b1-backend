using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Configuração da NF-e da filial (só STANDALONE). Tabela própria para o .pfx não viajar em cada
/// leitura de BRANCHS — que o SAPB1 também faz. A senha fica cifrada (AES-GCM, chave no
/// appsettings do servidor) e nunca sai pela API.
/// </summary>
[Table("BRANCH_NFE_SETTINGS")]
public class BranchNfeSettings
{
    [Key]
    [Column(TypeName = "VARCHAR(14)")]
    [ForeignKey(nameof(Branch))]
    public required string BranchCode { get; set; }

    public virtual Branch? Branch { get; set; }

    public NfeEnvironment Environment { get; set; } = NfeEnvironment.Homologation;

    public int Series { get; set; } = 1;

    /// <summary>Próximo número a reservar — ver <c>NfeNumberReservationService</c>.</summary>
    public int NextNumber { get; set; } = 1;

    [Column(TypeName = "VARBINARY(MAX)")]
    public byte[]? CertificatePfx { get; set; }

    [Column(TypeName = "VARBINARY(512)")]
    public byte[]? CertificatePasswordCipher { get; set; }

    [Column(TypeName = "VARCHAR(250)")]
    public string? CertificateSubject { get; set; }

    [Column(TypeName = "VARCHAR(14)")]
    public string? CertificateTaxId { get; set; }

    public DateTime? CertificateValidUntil { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }
}
