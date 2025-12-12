using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("PROCESSING_SERVICES_PARAMETERS")]
public class ProcessingServiceParameters
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int RowId { get; set; }
    
    [Column(TypeName = "VARCHAR(10) NOT NULL")]
    public string? ProcessingServiceCode { get; set; }
    public virtual ProcessingService? ProcessingService { get; set; }

    public int StartDay { get; set; } = 0;

    public int EndDay { get; set; } = 0;
    
    [Column(TypeName = "DECIMAL(18,8) DEFAULT 0")]
    public decimal Value { get; set; }
    
    public int Period { get; set; }
}