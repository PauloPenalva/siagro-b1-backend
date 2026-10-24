using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Parâmetros da EFD-Reinf por contribuinte (raiz do CNPJ) — dados do R-1000. Só STANDALONE.</summary>
[Table("REINF_SETTINGS")]
public class ReinfSettings
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    /// <summary>Raiz do CNPJ (8 dígitos). Única.</summary>
    [Column(TypeName = "VARCHAR(8)")] public string TaxIdRoot { get; set; } = string.Empty;
    /// <summary>Filial cujo certificado assina/transmite.</summary>
    [Column(TypeName = "VARCHAR(14)")] public string BranchCode { get; set; } = string.Empty;
    public ReinfEnvironment Environment { get; set; } = ReinfEnvironment.ProductionRestricted;
    /// <summary>classTrib (2 dígitos).</summary>
    [Column(TypeName = "VARCHAR(2)")] public string TaxClassification { get; set; } = string.Empty;
    public bool KeepsBookkeeping { get; set; }
    public bool PayrollRelief { get; set; }
    public bool FineExemptionAgreement { get; set; }
    public int? CompanySituation { get; set; }
    [Column(TypeName = "VARCHAR(70)")] public string ContactName { get; set; } = string.Empty;
    [Column(TypeName = "VARCHAR(11)")] public string ContactCpf { get; set; } = string.Empty;
    [Column(TypeName = "VARCHAR(13)")] public string? ContactPhone { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? ContactEmail { get; set; }
    /// <summary>Início da validade (AAAA-MM).</summary>
    [Column(TypeName = "VARCHAR(7)")] public string ValidFrom { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(70)")] public string? ClosingResponsibleName { get; set; }
    [Column(TypeName = "VARCHAR(11)")] public string? ClosingResponsibleCpf { get; set; }
    [Column(TypeName = "VARCHAR(13)")] public string? ClosingResponsiblePhone { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public string? ClosingResponsibleEmail { get; set; }

    public ReinfRegistrationStatus RegistrationStatus { get; set; } = ReinfRegistrationStatus.NotSent;
    [Column(TypeName = "VARCHAR(60)")] public string? RegistrationReceipt { get; set; }

    public DateTime UpdatedAt { get; set; }
    [Column(TypeName = "VARCHAR(100)")] public string? UpdatedBy { get; set; }
}
