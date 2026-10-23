using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>CT-e levado pelo MDF-e (infMunDescarga/infCTe). Um CT-e só pode estar em um MDF-e ativo por vez.</summary>
[Table("TRANSPORT_MANIFEST_CTES")]
public class TransportManifestCte
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    /// <summary>CT-e do Siagro. FK Restrict: CT-e vinculado a MDF-e não se exclui.</summary>
    public Guid TransportDocumentKey { get; set; }
    public virtual TransportDocument? TransportDocument { get; set; }

    public int LineNum { get; set; }
    [Column(TypeName = "VARCHAR(44)")] public required string CteAccessKey { get; set; }
    [Column(TypeName = "VARCHAR(7)")] public required string UnloadingCityIbge { get; set; }
    [Column(TypeName = "VARCHAR(60)")] public required string UnloadingCityName { get; set; }
    /// <summary>Averbações do seguro separadas por vírgula.</summary>
    [Column(TypeName = "VARCHAR(500)")] public string? Endorsements { get; set; }

    /// <summary>
    /// Mantido pelos serviços: false quando o MDF-e vira Voided/Cancelled/Closed (o rascunho excluído
    /// leva a linha junto). O índice único filtrado em TransportDocumentKey usa só este campo, porque
    /// filtro de índice não aceita NOT IN.
    /// </summary>
    public bool IsActive { get; set; } = true;
}
