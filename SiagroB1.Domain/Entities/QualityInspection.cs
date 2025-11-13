using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("QUALITY_INSPECTIONS")]
public class QualityInspection
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }
    
    [ForeignKey("WeighingTicket")]
    public Guid? WeighingTicketKey { get; set; }
    public virtual WeighingTicket? WeighingTicket { get; set; }
    
    [ForeignKey("QualityAttrib")]
    public required Guid QualityAttribKey { get; set; }
    public virtual QualityAttrib? QualityAttrib { get; set; }

    [Column(TypeName = "DECIMAL(18,1) DEFAULT 0")]
    public decimal Value { get; set; } = 0;
}