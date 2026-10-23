using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Configuração do MDF-e da filial (só STANDALONE). O certificado é o de <see cref="BranchNfeSettings"/>
/// e o RNTRC o de <see cref="BranchTransportSettings"/>; o ambiente e a numeração são próprios do MDF-e.
/// </summary>
[Table("BRANCH_MDFE_SETTINGS")]
public class BranchMdfeSettings
{
    [Key]
    [Column(TypeName = "VARCHAR(14)")]
    [ForeignKey(nameof(Branch))]
    public required string BranchCode { get; set; }

    public virtual Branch? Branch { get; set; }

    /// <summary>A filial emite MDF-e.</summary>
    public bool Enabled { get; set; }

    public NfeEnvironment Environment { get; set; } = NfeEnvironment.Homologation;

    public int DefaultSeries { get; set; } = 1;

    /// <summary>Tipo da carga predominante (tpCarga do MDF-e, 01 a 13); padrão 02 = granel líquido.</summary>
    [Column(TypeName = "VARCHAR(2)")]
    public string DefaultCargoType { get; set; } = "02";

    /// <summary>A filial exige CIOT (pagamento de frete) antes de emitir.</summary>
    public bool RequiresCiot { get; set; }

    public DateTime? UpdatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? UpdatedBy { get; set; }

    public virtual ICollection<BranchMdfeSeries> SeriesList { get; set; } = new List<BranchMdfeSeries>();
}
