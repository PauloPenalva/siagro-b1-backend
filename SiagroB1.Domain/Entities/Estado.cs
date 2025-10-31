using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("estados")]
public class Estado 
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("codigo")]
    public required string Codigo { get; set; }

    [Column("sigla")]
    public required string Sigla { get; set; }
}