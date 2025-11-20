using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Shared.Base;

public abstract class BaseEntity
{
    [Column(TypeName = "VARCHAR(14)", Order = 1)]
    public required string BranchCode { get; set; }
    
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid? Key { get; set; }
}
