using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Entities;

/// <summary>
/// cadastro de tipos de serviço de beneficiamento/armazenagem
/// </summary>
[Table("PROCESSING_SERVICES")]
public class ProcessingService
{
    [Key]
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public required string Code { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Description { get; set; }
    
    // public ProcessingServiceBillingType BillingType { get; set; }
    //
    // public int GraceDays { get; set; }
    //
    // [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    // public decimal FixedValue { get; set; }
    //
    // public ICollection<ProcessingServiceParameters> Parameters { get; set; } = [];
}