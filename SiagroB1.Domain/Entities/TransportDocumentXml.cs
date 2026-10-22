using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>XML guardado do CT-e (autorizado, cancelamento ou correção).</summary>
[Table("TRANSPORT_DOCUMENT_XMLS")]
public class TransportDocumentXml
{
    [Key]
    public Guid Key { get; set; } = Guid.NewGuid();

    public Guid TransportDocumentKey { get; set; }
    public virtual TransportDocument? TransportDocument { get; set; }

    public CteXmlKind Kind { get; set; }
    [Column(TypeName = "NVARCHAR(MAX)")] public required string Xml { get; set; }
}
