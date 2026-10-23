using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Parâmetros do SPED Fiscal (EFD ICMS/IPI) por filial (só STANDALONE): perfil, atividade, contabilista
/// (registro 0100) e o recolhimento do ICMS (registro E116). Uma linha por filial.
/// </summary>
[Table("SPED_FISCAL_SETTINGS")]
public class SpedFiscalSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column(TypeName = "VARCHAR(14)")]
    public string BranchCode { get; set; } = string.Empty;

    /// <summary>Perfil de apresentação: "A", "B" ou "C".</summary>
    [Column(TypeName = "VARCHAR(1)")]
    public string Profile { get; set; } = "A";

    /// <summary>Tipo de atividade: 0 industrial ou equiparado, 1 outros.</summary>
    public int ActivityType { get; set; } = 1;

    [Column(TypeName = "VARCHAR(100)")]
    public string AccountantName { get; set; } = string.Empty;

    /// <summary>CPF do contabilista, só dígitos.</summary>
    [Column(TypeName = "VARCHAR(11)")]
    public string AccountantCpf { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(15)")]
    public string AccountantCrc { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(14)")]
    public string? AccountantCnpj { get; set; }

    [Column(TypeName = "VARCHAR(8)")]
    public string? AccountantZipCode { get; set; }

    [Column(TypeName = "VARCHAR(60)")]
    public string? AccountantStreet { get; set; }

    [Column(TypeName = "VARCHAR(10)")]
    public string? AccountantNumber { get; set; }

    [Column(TypeName = "VARCHAR(60)")]
    public string? AccountantComplement { get; set; }

    [Column(TypeName = "VARCHAR(60)")]
    public string? AccountantDistrict { get; set; }

    [Column(TypeName = "VARCHAR(11)")]
    public string? AccountantPhone { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? AccountantEmail { get; set; }

    /// <summary>Código IBGE do município do contabilista.</summary>
    [Column(TypeName = "VARCHAR(7)")]
    public string? AccountantMunicipalityCode { get; set; }

    /// <summary>Código de receita do ICMS (E116 COD_REC).</summary>
    [Column(TypeName = "VARCHAR(30)")]
    public string IcmsRevenueCode { get; set; } = string.Empty;

    /// <summary>Dia do vencimento do ICMS no mês seguinte (1 a 31).</summary>
    public int IcmsDueDay { get; set; } = 10;

    public DateTime UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string UpdatedBy { get; set; } = string.Empty;
}
