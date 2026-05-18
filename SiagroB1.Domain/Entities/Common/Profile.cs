using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

[Table("PROFILES")]
public class Profile
{
    [Key]
    [Column(TypeName = "VARCHAR(50)")]
    public required string Code { get; set; }

    [Column(TypeName = "VARCHAR(254)")]
    public string? Description { get; set; }

    public virtual ICollection<ProfileRole> Roles { get; set; } = [];
}