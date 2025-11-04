using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace SiagroB1.Domain.Entities
{
    [Table("branchs")]
    public class Branch
    {
        [Key]
        [Column(name: "key", Order = 1)]
        public string? Key { get; set; }

        [Column("branch_name")]
        public required string BranchName { get; set; }
    }
}