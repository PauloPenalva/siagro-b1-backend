using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

/// <summary>Extrato do armazém (ou outra evidência) anexado à Conferência de Saldo. Opcional.</summary>
[Table("WAREHOUSE_RECONCILIATION_ATTACHMENTS")]
public class WarehouseReconciliationAttachment
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }

    public Guid WarehouseReconciliationKey { get; set; }
    public virtual WarehouseReconciliation? WarehouseReconciliation { get; set; }

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
