using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("TRUCK_DRIVERS")]
[Index("BranchKey",["Cpf"],  IsUnique = true)]
public class TruckDriver : BaseEntity
{
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(11) ")]
    public string? Cpf { get; set; }
}