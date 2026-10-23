using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>XML guardado do MDF-e (assinado, autorizado e eventos).</summary>
[Table("TRANSPORT_MANIFEST_XMLS")]
public class TransportManifestXml
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid ManifestKey { get; set; }
    public virtual TransportManifest? Manifest { get; set; }

    public MdfeXmlKind Kind { get; set; }
    [Column(TypeName = "NVARCHAR(MAX)")] public required string Xml { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
