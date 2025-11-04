using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Base;

namespace SiagroB1.Domain.Entities;

[Table("wharehouse")]
public class WhareHouse : BaseEntity<string>
{
    [Required(ErrorMessage = "Name is mandatory.")]
    [Column("wharehouse_name", TypeName = "varchar(100) not null")]
    public required string Name { get; set; }
    
    [Column("wharehouse_taxid", TypeName = "varchar(14) not null")]
    public string? TaxId { get; set; }

    [Column("wharehouse_inactive")] 
    public bool Inactive { get; set; } = false;
}