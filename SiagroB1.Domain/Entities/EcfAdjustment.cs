using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Ajuste do LALUR/LACS (adição, exclusão ou compensação de prejuízo) de um ano-calendário, por filial.
/// Alimenta M300/M350 e a conta da Parte B.
/// </summary>
[Table("ECF_ADJUSTMENTS")]
[Microsoft.EntityFrameworkCore.Index(nameof(BranchCode), nameof(Year))]
public class EcfAdjustment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    [Column(TypeName = "VARCHAR(14)")]
    public string BranchCode { get; set; } = string.Empty;

    public int Year { get; set; }

    /// <summary>0 = apuração anual; 1 a 4 = trimestre.</summary>
    public int Period { get; set; }

    public EcfAdjustmentTax Tax { get; set; } = EcfAdjustmentTax.Both;

    public EcfAdjustmentKind Kind { get; set; } = EcfAdjustmentKind.Addition;

    /// <summary>Código do lançamento na tabela dinâmica da ECF.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string TableCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(250)")]
    public string Description { get; set; } = string.Empty;

    [Column(TypeName = "DECIMAL(18,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? LedgerAccountCode { get; set; }

    [Column(TypeName = "VARCHAR(50)")]
    public string? PartBAccountCode { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? PartBAccountName { get; set; }
}
