using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Base
{
    public abstract class BaseEntity<ID>
    {
        [Column(TypeName = "VARCHAR(14)", Order = 1)]
        [ForeignKey("Branch")]
        public string? BranchKey { get; set; }
        public Branch? Branch { get; set; }
        
        [Key]
        [Column(TypeName = "VARCHAR(10) NOT NULL", Order = 2)]
        public ID? Key { get; set; }
    }
}