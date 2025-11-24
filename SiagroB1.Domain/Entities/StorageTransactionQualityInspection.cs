using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS")]
public class StorageTransactionQualityInspection
{
    [Key]
    public Guid? Key {  get; set; }
    
    public Guid? StorageTransactionKey { get; set; }
    public virtual StorageTransaction? StorageTransaction { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    [ForeignKey("QualityAttrib")]
    public required string QualityAttribCode { get; set; }
    public virtual QualityAttrib? QualityAttrib { get; set; }

    [Column(TypeName = "DECIMAL(18,1) DEFAULT 0")]
    [Range(0, double.MaxValue)]
    public decimal Value { get; set; } = 0;
    
    [Column(TypeName = "DECIMAL(18,3) DEFAULT 0")]
    public decimal LossValue { get; set; } = 0;
}