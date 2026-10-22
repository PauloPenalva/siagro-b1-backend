using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Série do CT-e da filial, com o contador próprio do próximo número — ver
/// <c>CteNumberReservationService</c>. Chave composta (BranchCode, Series).
/// </summary>
[Table("BRANCH_CTE_SERIES")]
public class BranchCteSeries
{
    [Column(TypeName = "VARCHAR(14)")]
    [ForeignKey(nameof(Settings))]
    public required string BranchCode { get; set; }

    public int Series { get; set; }

    [Column(TypeName = "VARCHAR(60)")]
    public required string Description { get; set; }

    /// <summary>Próximo número a reservar nesta série.</summary>
    public int NextNumber { get; set; } = 1;

    public virtual BranchCteSettings? Settings { get; set; }
}
