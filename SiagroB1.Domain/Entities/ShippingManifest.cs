using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Entities;

[Table("SHIPPING_MANIFESTS")]
public class ShippingManifest : BaseEntity
{
    [Column(TypeName = "VARCHAR(15) NOT NULL")]
    public required string ManifestNumber { get; set; } // "ROM-001/2024"
    
    public DateTime ManifestDate { get; set; } = DateTime.Now;
    
    public required Guid ShipmentReleaseKey { get; set; }
    
    // Quantidades
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal GrossWeight { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal TareWeight { get; set; }
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal NetWeight => GrossWeight - TareWeight;
    
    // Controle
    public ManifestStatus Status { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string CreatedBy { get; set; } = string.Empty;
}