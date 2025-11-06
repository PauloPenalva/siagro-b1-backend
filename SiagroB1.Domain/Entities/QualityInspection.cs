using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("QUALITY_INSPECTIONS")]
public class QualityInspection
{
    [Column(TypeName = "VARCHAR(14)", Order = 1)]
    public string? BranchKey { get; set; }
    
    [MaxLength(10)]
    [Column(TypeName = "VARCHAR(10)", Order = 2)]
    [ForeignKey("WeighingTicket")]
    public string? WeighingTicketKey { get; set; }
    public virtual WeighingTicket? WeighingTicket { get; set; }
    
    [MaxLength(10)]
    [Column(TypeName = "VARCHAR(10)", Order = 3)]
    [ForeignKey("QualityAttrib")]
    public required string QualityAttribKey { get; set; }
    public virtual QualityAttrib? QualityAttrib { get; set; }

    [Column(TypeName = "DECIMAL(18,1) DEFAULT 0")]
    public decimal Value { get; set; } = 0;
}