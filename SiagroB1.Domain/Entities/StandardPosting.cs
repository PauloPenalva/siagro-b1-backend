using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Lançamento padrão configurado (CT5 do Protheus) para um código do catálogo. Sem registro ou inativo, o evento
/// não contabiliza. As sequências escolhem fontes de uma lista fechada, nunca expressões.
/// </summary>
[Table("STANDARD_POSTINGS")]
public class StandardPosting
{
    [Key]
    [Column(TypeName = "VARCHAR(3)")]
    public string Code { get; set; } = string.Empty;

    public bool Active { get; set; } = true;

    public DateTime UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string UpdatedBy { get; set; } = string.Empty;

    public List<StandardPostingLine> Lines { get; set; } = new();
}

/// <summary>Sequência de um lançamento padrão (CT5_SEQUEN).</summary>
[Table("STANDARD_POSTING_LINES")]
public class StandardPostingLine
{
    [Key]
    public int Id { get; set; }

    [Column(TypeName = "VARCHAR(3)")]
    public string Code { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(3)")]
    public string Sequence { get; set; } = string.Empty;

    public JournalEntryLineType EntryType { get; set; } = JournalEntryLineType.Double;

    [Column(TypeName = "VARCHAR(40)")]
    public string? DebitAccountSource { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? DebitAccountCode { get; set; }

    [Column(TypeName = "VARCHAR(40)")]
    public string? CreditAccountSource { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? CreditAccountCode { get; set; }

    [Column(TypeName = "VARCHAR(40)")]
    public string ValueSource { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(200)")]
    public string HistoryTemplate { get; set; } = string.Empty;

    public DimensionSource DebitCostCenterSource { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? DebitCostCenterCode { get; set; }

    public DimensionSource CreditCostCenterSource { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? CreditCostCenterCode { get; set; }

    public DimensionSource DebitItemSource { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? DebitItemCode { get; set; }

    public DimensionSource CreditItemSource { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? CreditItemCode { get; set; }

    public bool Active { get; set; } = true;
}
