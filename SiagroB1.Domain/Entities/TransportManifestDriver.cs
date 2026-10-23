using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Condutor do MDF-e (inclusive os incluídos por evento).</summary>
[Table("TRANSPORT_MANIFEST_DRIVERS")]
public class TransportManifestDriver
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    public int LineNum { get; set; }
    [Column(TypeName = "VARCHAR(11)")] public required string Cpf { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public required string Name { get; set; }
    /// <summary>True quando entrou por evento de inclusão de condutor depois da autorização.</summary>
    public bool IncludedByEvent { get; set; }
    /// <summary>Sequência (nSeqEvento) do evento 110114 que incluiu o condutor; nulo para os condutores da emissão.</summary>
    public int? EventSequence { get; set; }
}
