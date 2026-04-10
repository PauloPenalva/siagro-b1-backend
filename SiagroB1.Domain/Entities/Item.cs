using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities;

[Table("ITEMS")]
public class Item
{
    [Key]
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string ItemCode { get; set; }

    [Column(TypeName = "VARCHAR(200) NOT NULL")]
    public required string ItemName { get; set; }

    public short? ItmsGrpCod { get; set; } = 105;

    [Column(TypeName = "VARCHAR(3)")]
    public string? Enabled { get; set; } = "SIM";
}