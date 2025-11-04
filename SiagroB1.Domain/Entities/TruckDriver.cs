using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("truck_drivers")]
public class TruckDriver : BaseEntity<string>
{
    [Column("Name")]
    public required string Name { get; set; }

    [Column("cpf")]
    public string? Cpf { get; set; }
}