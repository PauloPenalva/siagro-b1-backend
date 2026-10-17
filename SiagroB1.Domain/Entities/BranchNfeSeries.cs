using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Série da NF-e da filial, com o contador próprio do próximo número — ver
/// <c>NfeNumberReservationService</c>. Chave composta (BranchCode, Series).
/// </summary>
[Table("BRANCH_NFE_SERIES")]
public class BranchNfeSeries
{
    [Column(TypeName = "VARCHAR(14)")]
    [ForeignKey(nameof(Settings))]
    public required string BranchCode { get; set; }

    public int Series { get; set; }

    [Column(TypeName = "VARCHAR(60)")]
    public required string Description { get; set; }

    /// <summary>Próximo número a reservar nesta série.</summary>
    public int NextNumber { get; set; } = 1;

    public virtual BranchNfeSettings? Settings { get; set; }
}
