using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Configuração do CT-e da filial (só STANDALONE). O certificado é o de <see cref="BranchNfeSettings"/>
/// e o RNTRC o de <see cref="BranchTransportSettings"/>; o ambiente e a numeração são próprios do CT-e.
/// </summary>
[Table("BRANCH_CTE_SETTINGS")]
public class BranchCteSettings
{
    [Key]
    [Column(TypeName = "VARCHAR(14)")]
    [ForeignKey(nameof(Branch))]
    public required string BranchCode { get; set; }

    public virtual Branch? Branch { get; set; }

    /// <summary>A filial emite CT-e.</summary>
    public bool Enabled { get; set; }

    public NfeEnvironment Environment { get; set; } = NfeEnvironment.Homologation;

    public int DefaultSeries { get; set; } = 1;

    /// <summary>Código da unidade da quantidade da carga (00 M3, 01 KG, 02 TON, 03 UNIDADE, 04 LITROS, 05 MMBTU).</summary>
    [Column(TypeName = "VARCHAR(2)")]
    public string DefaultQuantityUnit { get; set; } = "00";

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }

    public virtual ICollection<BranchCteSeries> SeriesList { get; set; } = new List<BranchCteSeries>();
}
