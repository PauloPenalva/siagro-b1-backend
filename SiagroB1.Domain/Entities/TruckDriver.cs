using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("TRUCK_DRIVERS")]
public class TruckDriver 
{
    [Key]
    [Column(TypeName = "VARCHAR(11) NOT NULL")]
    public required string Cpf { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }
}