using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Controle da Distribuição de DF-e por filial (só STANDALONE): último NSU lido e quando a SEFAZ
/// permite consultar de novo (cStat 137/656 exigem espera de 1h).
/// </summary>
[Table("BRANCH_DFE_SYNC_STATES")]
public class BranchDfeSyncState
{
    [Key]
    [Column(TypeName = "VARCHAR(14)")]
    public required string BranchCode { get; set; }

    public long LastNsu { get; set; }

    public long MaxNsu { get; set; }

    public DateTimeOffset? LastQueryAt { get; set; }

    public DateTimeOffset? NextQueryAllowedAt { get; set; }

    /// <summary>Horários "HH:mm" separados por ";", ordenados (ex.: "12:00;20:00"). Nulo/vazio = sem consulta automática.</summary>
    [Column(TypeName = "VARCHAR(200)")]
    public string? ScheduleTimes { get; set; }

    public DateTimeOffset? LastScheduledRunAt { get; set; }
}
