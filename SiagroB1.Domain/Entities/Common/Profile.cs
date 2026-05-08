using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SiagroB1.Domain.Entities.Common;

[Table("PROFILES")]
public class Profile
{
    [Key]
    public Guid Id { get; set; } =  Guid.NewGuid();

    [Column(TypeName = "VARCHAR(100) NOT_NULL")]
    public required string Name { get; set; }

    [Column(TypeName = "VARCHAR(254)")]
    public string? Description { get; set; }

    public virtual ICollection<ProfileRole> Roles { get; set; } = [];
}