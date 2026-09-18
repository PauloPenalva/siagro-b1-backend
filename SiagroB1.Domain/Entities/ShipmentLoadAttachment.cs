using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// Documento anexado à carga: tickets de carga e de descarga, nota fiscal, conhecimento de frete
/// ou outro (GAC-1171). Molde de <c>WarehouseReconciliationAttachment</c>, mais o tipo.
/// </summary>
[Table("SHIPMENT_LOAD_ATTACHMENTS")]
[Index(nameof(ShipmentLoadKey))]
public class ShipmentLoadAttachment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid? ShipmentLoadKey { get; set; }
    public virtual ShipmentLoad? ShipmentLoad { get; set; }

    public ShipmentLoadAttachmentType AttachmentType { get; set; } = ShipmentLoadAttachmentType.Other;

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string FileName { get; set; }

    [Column(TypeName = "VARBINARY(MAX)")]
    public required byte[] FileData { get; set; }

    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string ContentType { get; set; }

    public DateTime? CreatedAt { get; set; }

    [Column(TypeName = "VARCHAR(100)")]
    public string? CreatedBy { get; set; }
}
