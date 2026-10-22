using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>Uma execução da contabilização off-line (simulação ou processamento), com o relatório de saída.</summary>
[Table("OFFLINE_POSTING_RUNS")]
public class OfflinePostingRun
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    [Column(TypeName = "DATE")]
    public DateTime DateFrom { get; set; }

    [Column(TypeName = "DATE")]
    public DateTime DateTo { get; set; }

    /// <summary>Filial filtrada; nula = todas.</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string? BranchCode { get; set; }

    /// <summary>Módulos (<see cref="OfflinePostingModule"/>) separados por vírgula, ex. "1,2,3".</summary>
    [Column(TypeName = "VARCHAR(20)")]
    public string Modules { get; set; } = string.Empty;

    public bool Simulation { get; set; }

    public OfflinePostingRunStatus Status { get; set; } = OfflinePostingRunStatus.Queued;

    [Column(TypeName = "VARCHAR(100)")]
    public string RequestedBy { get; set; } = string.Empty;

    public DateTime RequestedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    public int PostedCount { get; set; }

    public int ErrorCount { get; set; }

    public int SkippedCount { get; set; }

    [Column(TypeName = "VARCHAR(2000)")]
    public string? FailureMessage { get; set; }

    public virtual ICollection<OfflinePostingRunItem> Items { get; set; } = new List<OfflinePostingRunItem>();
}
