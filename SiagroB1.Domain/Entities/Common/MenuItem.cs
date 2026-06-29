using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Domain.Entities.Common;

[Table("MENU_ITEMS")]
[Index(nameof(Key), IsUnique = true)]
public class MenuItem
{
    [Key]
    [Column(TypeName = "VARCHAR(50) NOT NULL")]
    public required string Key { get; set; }
    
    [Column(TypeName = "VARCHAR(100) NOT NULL")]
    public required string Title { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string Icon { get; set; } = "sap-icon://folder-blank";
    
    public bool Enabled { get; set; } = true;
    
    public bool Expanded { get; set; } = false;
    
    public int Order { get; set; }

    [ForeignKey(nameof(Parent))]
    public string? ParentKey { get; set; }
    
    public virtual MenuItem? Parent { get; set; }

    public virtual ICollection<MenuItem> Children { get; set; } = new List<MenuItem>();
}