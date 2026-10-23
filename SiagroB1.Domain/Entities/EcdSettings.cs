using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Parâmetros da ECD (SPED Contábil) por filial (só STANDALONE): livro, NIRE, porte, plano referencial
/// (registros 0000/I030) e o signatário (J930). Uma linha por filial.
/// </summary>
[Table("ECD_SETTINGS")]
public class EcdSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column(TypeName = "VARCHAR(14)")]
    public string BranchCode { get; set; } = string.Empty;

    /// <summary>Número de ordem do livro (I030 NUM_ORD), maior que zero.</summary>
    public int BookNumber { get; set; } = 1;

    public bool HasNire { get; set; }

    /// <summary>NIRE, 11 dígitos; só quando <see cref="HasNire"/>.</summary>
    [Column(TypeName = "VARCHAR(11)")]
    public string? Nire { get; set; }

    public DateTime? ConstitutiveActsDate { get; set; }

    /// <summary>Empresa de grande porte (0000 IND_GRANDE_PORTE).</summary>
    public bool LargeCompany { get; set; }

    /// <summary>Código do plano referencial (COD_PLAN_REF), "1" a "10".</summary>
    [Column(TypeName = "VARCHAR(2)")]
    public string ReferentialPlanCode { get; set; } = "1";

    [Column(TypeName = "VARCHAR(100)")]
    public string SignerName { get; set; } = string.Empty;

    /// <summary>CPF do signatário, só dígitos.</summary>
    [Column(TypeName = "VARCHAR(11)")]
    public string SignerCpf { get; set; } = string.Empty;

    /// <summary>Qualificação do assinante (J930 COD_ASSIN), 3 dígitos; 203 = diretor.</summary>
    [Column(TypeName = "VARCHAR(3)")]
    public string SignerQualificationCode { get; set; } = "203";

    [Column(TypeName = "VARCHAR(100)")]
    public string SignerEmail { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(11)")]
    public string? SignerPhone { get; set; }

    public DateTime UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string UpdatedBy { get; set; } = string.Empty;
}
