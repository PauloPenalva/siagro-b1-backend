using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

[Table("MENU_ITEMS")]
public class MenuItem
{
    [Key]
    public Guid? Id { get; set; } =  Guid.NewGuid();
    
    [Column(TypeName = "VARCHAR(100)")]
    public required string Title { get; set; }
    
    [Column(TypeName = "VARCHAR(50)")]
    public string? Key { get; set; }
    
    [Column(TypeName = "VARCHAR(100)")]
    public string Icon { get; set; } = "sap-icon://folder-blank";
    
    public bool Enabled { get; set; } = true;
    
    public bool Expanded { get; set; } = false;
    
    public int Order { get; set; }

    public Guid? ParentId { get; set; }
    
    public virtual MenuItem? Parent { get; set; }

    public virtual ICollection<MenuItem> Children { get; set; } = new List<MenuItem>();
}