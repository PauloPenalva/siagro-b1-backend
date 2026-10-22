using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Linha do lançamento contábil — equivale a um registro da CT2.</summary>
[Table("JOURNAL_ENTRY_LINES")]
public class JournalEntryLine
{
    [Key]
    public int Id { get; set; }

    public int DocEntry { get; set; }

    public int LineNumber { get; set; }

    public JournalEntryLineType EntryType { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? DebitAccountCode { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? CreditAccountCode { get; set; }

    [Column(TypeName = "decimal(19,2)")]
    public decimal Amount { get; set; }

    [Column(TypeName = "VARCHAR(40) NOT NULL")]
    public string History { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(20)")]
    public string? DebitCostCenterCode { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? CreditCostCenterCode { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? DebitAccountingItemCode { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? CreditAccountingItemCode { get; set; }

    /// <summary>Sequência do lançamento padrão (CT5_SEQUEN) que gerou a linha.</summary>
    [Column(TypeName = "VARCHAR(3)")]
    public string? StandardPostingSequence { get; set; }

    /// <summary>Parceiro da linha, para razão por cliente/fornecedor (não existe na CT2).</summary>
    [Column(TypeName = "VARCHAR(50)")]
    public string? BusinessPartnerCode { get; set; }
}
