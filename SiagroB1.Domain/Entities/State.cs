using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("states")]
public class State 
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("key")]
    public required string Key { get; set; }

    [Column("name", TypeName = "varchar(100)")]
    public required string Name { get; set; }
    
    [Column("abbreviation", TypeName = "varchar(2)")]
    public required string Abbreviation { get; set; }
}