using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento contábil (data + lote + sublote + documento da CT2 do Protheus). Imutável depois de gravado:
/// a correção é feita por estorno (<see cref="ReversalOfDocEntry"/>).
/// </summary>
[Table("JOURNAL_ENTRIES")]
public class JournalEntry
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int DocEntry { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime PostingDate { get; set; }

    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string BranchCode { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(6) NOT NULL")]
    public string Batch { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(3) NOT NULL")]
    public string SubBatch { get; set; } = "001";

    [Column(TypeName = "VARCHAR(6) NOT NULL")]
    public string DocumentNumber { get; set; } = string.Empty;

    public JournalEntryOrigin Origin { get; set; }

    /// <summary>Código do lançamento padrão (CT2_LP); vazio no manual.</summary>
    [Column(TypeName = "VARCHAR(3)")]
    public string? StandardPostingCode { get; set; }

    [Column(TypeName = "VARCHAR(40)")]
    public string? SourceDocumentType { get; set; }

    [Column(TypeName = "VARCHAR(60)")]
    public string? SourceDocumentKey { get; set; }

    [Column(TypeName = "VARCHAR(20)")]
    public string? SourceEvent { get; set; }

    public int? ReversalOfDocEntry { get; set; }

    public int? ReversedByDocEntry { get; set; }

    [Column(TypeName = "VARCHAR(250)")]
    public string? Comments { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public string CreatedBy { get; set; } = string.Empty;

    public List<JournalEntryLine> Lines { get; set; } = new();
}
