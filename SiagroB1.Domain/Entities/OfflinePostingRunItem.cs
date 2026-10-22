using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Um documento do relatório da contabilização off-line.</summary>
[Table("OFFLINE_POSTING_RUN_ITEMS")]
public class OfflinePostingRunItem
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid RunKey { get; set; }

    /// <summary>Ordem cronológica de processamento.</summary>
    public int Sequence { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime EventDate { get; set; }

    [Column(TypeName = "VARCHAR(40)")]
    public string DocumentType { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(40)")]
    public string DocumentKey { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(60)")]
    public string DocumentNumber { get; set; } = string.Empty;

    [Column(TypeName = "VARCHAR(20)")]
    public string BranchCode { get; set; } = string.Empty;

    /// <summary>Códigos LP tentados, separados por vírgula.</summary>
    [Column(TypeName = "VARCHAR(40)")]
    public string Codes { get; set; } = string.Empty;

    public OfflinePostingItemResult Result { get; set; }

    [Column(TypeName = "VARCHAR(2000)")]
    public string? Message { get; set; }

    /// <summary>DocEntry dos lançamentos gravados, separados por vírgula.</summary>
    [Column(TypeName = "VARCHAR(200)")]
    public string? JournalEntryDocEntries { get; set; }
}
