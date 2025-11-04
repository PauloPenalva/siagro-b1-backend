using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Base
{
    public abstract class BaseEntity<ID>
    {
        [Column(name: "branch_key", Order = 1)]
        [ForeignKey("Branch")]
        public string? BranchKey { get; set; }
        public Branch? Branch { get; set; }
        
        [Key]
        [Column(name: "key", Order = 2)]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public ID? Key { get; set; }
    }
}