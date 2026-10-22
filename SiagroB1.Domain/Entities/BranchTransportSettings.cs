using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Configuração de transporte da filial (só STANDALONE): RNTRC, seguro padrão do CT-e/MDF-e e
/// credenciais da AT&amp;M para a averbação. A senha da AT&amp;M fica cifrada (mesma chave do
/// certificado) e nunca sai pela API.
/// </summary>
[Table("BRANCH_TRANSPORT_SETTINGS")]
public class BranchTransportSettings
{
    [Key]
    [Column(TypeName = "VARCHAR(14)")]
    [ForeignKey(nameof(Branch))]
    public required string BranchCode { get; set; }
    public virtual Branch? Branch { get; set; }

    /// <summary>RNTRC da filial transportadora.</summary>
    [Column(TypeName = "VARCHAR(8)")]
    public string? CompanyRntrc { get; set; }

    public InsuranceResponsible? InsuranceResponsible { get; set; }

    /// <summary>CNPJ da seguradora, só dígitos.</summary>
    [Column(TypeName = "VARCHAR(14)")]
    public string? InsurerTaxId { get; set; }

    [Column(TypeName = "VARCHAR(115)")]
    public string? InsurerName { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? PolicyNumber { get; set; }

    public bool AverbaEnabled { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? AverbaUser { get; set; }

    [Column(TypeName = "VARBINARY(512)")]
    public byte[]? AverbaPasswordCipher { get; set; }

    /// <summary>Código da empresa na AT&amp;M.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? AverbaCode { get; set; }

    public NfeEnvironment AverbaEnvironment { get; set; } = NfeEnvironment.Production;

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }
}
