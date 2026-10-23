using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Parâmetros da ECF (Escrituração Contábil Fiscal, lucro real) por filial (só STANDALONE):
/// forma de apuração, CNAE e qualificação da PJ (registros 0010/0030). Uma linha por filial.
/// </summary>
[Table("ECF_SETTINGS")]
public class EcfSettings
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column(TypeName = "VARCHAR(14)")]
    public string BranchCode { get; set; } = string.Empty;

    public EcfAssessmentForm AssessmentForm { get; set; } = EcfAssessmentForm.Annual;

    /// <summary>CNAE principal (0030), 7 dígitos.</summary>
    [Column(TypeName = "VARCHAR(7)")]
    public string MainCnae { get; set; } = string.Empty;

    /// <summary>Qualificação da pessoa jurídica (0030 COD_QUALIF_PJ), 2 dígitos; "01" = PJ em geral.</summary>
    [Column(TypeName = "VARCHAR(2)")]
    public string LegalEntityQualification { get; set; } = "01";

    /// <summary>Natureza jurídica (0030 COD_NAT, tabela da RFB), 4 dígitos; "2062" = sociedade empresária limitada.</summary>
    [Column(TypeName = "VARCHAR(4)")]
    public string NatureCode { get; set; } = "2062";

    /// <summary>Hash da ECF anterior (0000 HASH_ECF_ANTERIOR), quando há retificação/ECF anterior.</summary>
    [Column(TypeName = "VARCHAR(40)")]
    public string? PreviousEcfHash { get; set; }

    public DateTime UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string UpdatedBy { get; set; } = string.Empty;
}
