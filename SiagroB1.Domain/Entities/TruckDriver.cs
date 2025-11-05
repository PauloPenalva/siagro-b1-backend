using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("TRUCK_DRIVERS")]
public class TruckDriver : BaseEntity<string>
{
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(11) ")]
    public string? Cpf { get; set; }
}